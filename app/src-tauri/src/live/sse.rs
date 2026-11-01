//! A server-sent events parser, fed bytes as they arrive (the WHATWG event stream format): `event` and `data` fields,
//! comments, and lines ending in CR, LF or CRLF, even when a chunk ends between the two.

/// One dispatched event. `event` is "message" when the stream named none.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct SseEvent {
    pub event: String,
    pub data: String,
}

/// A line longer than this is dropped rather than buffered without end.
const MAX_LINE: usize = 1024 * 1024;

#[derive(Debug, Default)]
pub struct SseParser {
    line: Vec<u8>,
    overlong: bool,
    /// The last byte was a CR, so an LF that follows ends nothing.
    after_cr: bool,
    started: bool,
    event: String,
    data: String,
    has_data: bool,
}

impl SseParser {
    pub fn push(&mut self, bytes: &[u8]) -> Vec<SseEvent> {
        let mut events = Vec::new();
        let mut bytes = bytes;
        if !self.started && !bytes.is_empty() {
            self.started = true;
            // A UTF-8 byte order mark may start the stream, and only there.
            bytes = bytes.strip_prefix(b"\xEF\xBB\xBF").unwrap_or(bytes);
        }
        for &byte in bytes {
            match byte {
                b'\n' if self.after_cr => self.after_cr = false,
                b'\r' | b'\n' => {
                    self.after_cr = byte == b'\r';
                    self.end_line(&mut events);
                }
                _ => {
                    self.after_cr = false;
                    if self.line.len() < MAX_LINE {
                        self.line.push(byte);
                    } else {
                        self.overlong = true;
                    }
                }
            }
        }
        events
    }

    fn end_line(&mut self, events: &mut Vec<SseEvent>) {
        let line = std::mem::take(&mut self.line);
        if std::mem::take(&mut self.overlong) {
            return;
        }
        if line.is_empty() {
            self.dispatch(events);
            return;
        }
        if line[0] == b':' {
            return;
        }
        let line = String::from_utf8_lossy(&line);
        let (field, value) = match line.split_once(':') {
            Some((field, value)) => (field, value.strip_prefix(' ').unwrap_or(value)),
            None => (line.as_ref(), ""),
        };
        match field {
            "event" => value.clone_into(&mut self.event),
            "data" => {
                if self.has_data {
                    self.data.push('\n');
                }
                self.data.push_str(value);
                self.has_data = true;
            }
            // Ids and retry times don't matter here: the sidecar sends neither, and every reconnect starts with hello.
            _ => {}
        }
    }

    fn dispatch(&mut self, events: &mut Vec<SseEvent>) {
        let event = std::mem::take(&mut self.event);
        let data = std::mem::take(&mut self.data);
        if std::mem::take(&mut self.has_data) {
            events.push(SseEvent { event: if event.is_empty() { "message".to_owned() } else { event }, data });
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn event(name: &str, data: &str) -> SseEvent {
        SseEvent { event: name.to_owned(), data: data.to_owned() }
    }

    fn parse_all(chunks: &[&[u8]]) -> Vec<SseEvent> {
        let mut parser = SseParser::default();
        chunks.iter().flat_map(|chunk| parser.push(chunk)).collect()
    }

    #[test]
    fn reads_events_as_the_sidecar_writes_them() {
        let stream = b"event: hello\ndata: {\"live\":[]}\n\nevent: progress\ndata: {\"crawlId\":\"a\"}\n\n";
        assert_eq!(parse_all(&[stream]), [event("hello", r#"{"live":[]}"#), event("progress", r#"{"crawlId":"a"}"#)]);
    }

    #[test]
    fn comments_are_keep_alives_and_dispatch_nothing() {
        assert_eq!(parse_all(&[b": keep-alive\n\n: another\n\n"]), []);
    }

    #[test]
    fn an_event_split_anywhere_across_chunks_is_the_same_event() {
        let stream: &[u8] = b"event: crawl\r\ndata: {\"crawlId\":\"a\",\"status\":\"running\"}\r\n\r\n";
        let whole = parse_all(&[stream]);
        assert_eq!(whole, [event("crawl", r#"{"crawlId":"a","status":"running"}"#)]);
        for split in 1..stream.len() {
            assert_eq!(parse_all(&[&stream[..split], &stream[split..]]), whole, "split at {split}");
        }
        let bytes: Vec<&[u8]> = stream.chunks(1).collect();
        assert_eq!(parse_all(&bytes), whole);
    }

    #[test]
    fn any_line_ending_ends_a_line() {
        let expected = [event("a", "1"), event("b", "2")];
        assert_eq!(parse_all(&[b"event: a\rdata: 1\r\revent: b\rdata: 2\r\r"]), expected);
        assert_eq!(parse_all(&[b"event: a\ndata: 1\n\nevent: b\r\ndata: 2\r\n\r\n"]), expected);
    }

    #[test]
    fn data_lines_join_with_newlines_and_only_one_space_is_stripped() {
        assert_eq!(parse_all(&[b"data:first\ndata:  second\ndata\n\n"]), [event("message", "first\n second\n")]);
    }

    #[test]
    fn an_event_without_data_is_not_dispatched_and_names_do_not_leak() {
        assert_eq!(parse_all(&[b"event: lonely\n\ndata: x\n\n"]), [event("message", "x")]);
    }

    #[test]
    fn ids_retry_and_unknown_fields_are_ignored() {
        assert_eq!(
            parse_all(&[b"id: 7\nretry: 100\nfoo: bar\nevent: library\ndata: {}\n\n"]),
            [event("library", "{}")]
        );
    }

    #[test]
    fn a_leading_byte_order_mark_is_dropped() {
        assert_eq!(parse_all(&[b"\xEF\xBB\xBF", b"data: x\n\n"]), [event("message", "x")]);
    }

    #[test]
    fn an_unfinished_event_waits_for_its_blank_line() {
        let mut parser = SseParser::default();
        assert_eq!(parser.push(b"event: crawl\ndata: {}\n"), []);
        assert_eq!(parser.push(b"\n"), [event("crawl", "{}")]);
    }

    #[test]
    fn an_overlong_line_is_dropped_and_parsing_carries_on() {
        let mut stream = b"data: ".to_vec();
        stream.extend(std::iter::repeat_n(b'x', MAX_LINE + 5));
        stream.extend_from_slice(b"\ndata: ok\n\n");
        assert_eq!(parse_all(&[&stream]), [event("message", "ok")]);
    }
}
