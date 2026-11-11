P = '../docs/v2-sidecar-api.md'

EDITS = [
(P,
'''    | `invalid-options` | 422 | Validation failed |
''',
'''    | `invalid-options` | 422 | Validation failed |
    | `invalid-settings` | 422 | The analysis's limits can't be saved as sent: `errors` is keyed by limit (E6) |
'''),
(P,
'''  - or the analysis ran with other thresholds than the sidecar's.
''',
'''  - or the analysis ran with other thresholds than the sidecar's, which Settings can change
    ([Analysis settings](#analysis-settings-e6)).
'''),
(P,
'''### Data (E1: the readers that exist today)
''',
'''### Analysis settings (E6)

The limits every analysis here judges by ("a title longer than this is too long"), for Settings to show and change.
They're the engine's until the user saves their own, which are kept in the state folder (`analysis-settings.json`)
and outlive a restart. The names are the JSON names of the engine's `AnalysisSettings` properties, which are also
what each analysis stores in its crawl file.

| Method and path | Does |
| --- | --- |
| `GET /api/analysis-settings` | The limits in force, with what each is |
| `PUT /api/analysis-settings` | Saves limits, and answers as `GET` does. Every limit is optional: one left out, or null, takes the engine's value |
| `POST /api/analysis-settings/validate` | `{ errors: { <limit>: [messages] } }`, 200: what a save of the same body would refuse, as it's typed. Nothing is saved |
| `POST /api/analysis-settings/reset` | Body `{ "limits": ["titleMaxLength"] }`: puts those limits back to the engine's values and leaves the rest; with no body, or none named, all of them. Answers as `GET` does |

```json
{"settings":{"titleMinLength":30,"titleMaxLength":60,"descriptionMinLength":70,"descriptionMaxLength":155,"minWordCount":200,
             "duplicateMinWordCount":50,"nearDuplicateMaxBits":24,"imageAltMaxLength":125,"largeImageBytes":204800,
             "largePageBytes":1048576,"slowResponseMs":1000,"maxUrlLength":115},
 "limits":[{"id":"titleMaxLength","value":60,"default":60,"unit":"characters","min":1,"max":10000,
            "description":"A title longer than this is flagged as too long: search results cut it short."}],
 "isDefault":true}
```

- **`settings`** is each limit's value by name: the body `PUT` and `validate` take. **`limits`** lists every limit in
  the order Settings shows them, each with its `value` (the same number), the engine's `default`, its `unit`
  (`characters`, `words`, `bits`, `bytes` or `ms`), the `min` and `max` it may be set within, and a `description`: one
  sentence saying what it does, to show as it is. **`isDefault`** is true while every limit is at the engine's value.

  | Limit | Default | Unit | Range | A page, image or URL is flagged when |
  | --- | --- | --- | --- | --- |
  | `titleMinLength` | 30 | characters | 0 to 10,000 | its title is shorter |
  | `titleMaxLength` | 60 | characters | 1 to 10,000 | its title is longer |
  | `descriptionMinLength` | 70 | characters | 0 to 10,000 | its meta description is shorter |
  | `descriptionMaxLength` | 155 | characters | 1 to 10,000 | its meta description is longer |
  | `minWordCount` | 200 | words | 0 to 1,000,000 | it has fewer words of main content (thin) |
  | `duplicateMinWordCount` | 50 | words | 0 to 1,000,000 | (a page with fewer words is left out of the duplicate content checks) |
  | `nearDuplicateMaxBits` | 24 | bits | 0 to 32 | its content fingerprint is within this many bits, of 256, of another page's |
  | `imageAltMaxLength` | 125 | characters | 1 to 10,000 | an image's alt text is longer |
  | `largeImageBytes` | 204,800 | bytes | 1 to 10 GB | an image is larger |
  | `largePageBytes` | 1,048,576 | bytes | 1 to 10 GB | its HTML is larger |
  | `slowResponseMs` | 1,000 | ms | 1 to 3,600,000 | it takes longer to answer |
  | `maxUrlLength` | 115 | characters | 1 to 10,000 | the URL is longer |

  The inspector's `limits` are these as the URL's analysis ran with them, under the inspector's own shorter names.
- **Checked limit by limit.** A save that can't be made is `invalid-settings` (422) with `errors` keyed by the limit, and
  saves nothing: a value outside its range ("This must be from 1 to 10,000 characters."), one that isn't a whole
  number (text, a fraction, true: a field error here, not a 400, as an option of the wrong type is), and a longest
  length that isn't more than its shortest, reported on the longest (`titleMaxLength`, `descriptionMaxLength`) and
  judged with the engine's value for whichever of the two wasn't sent. A limit that doesn't exist, or a body that
  isn't an object of limits, is a `bad-request` (400), as a property the API doesn't know is everywhere. `reset` with
  a name that's no limit's is `invalid-settings` on `limits[<index>]`, and changes nothing.
- **Resetting one of a pair** that would leave the longest under the shortest (the shortest title raised to 70, the
  longest put back to 60) puts both back.
- **What a change does to the crawls already analysed.** An analysis stores the limits it ran with, and one that ran
  with other limits than those now in force is out of date: `outOfDate` on its library item and `outOfDateReasons`
  `settingsChanged` on `/analysis`, from the next read of either, for every crawl, open or not. Nothing else changes
  until it's analysed again: its counts, its URL summary and its grids stay as they are behind the notice. Putting the
  limits back makes it stand again. No event is sent: the UI reads the library again after its own save.
- **An analysis under way keeps the limits it began with.** It takes them once, as it starts, runs to its end with
  them and stores them: limits saved meanwhile reach the next analysis, and the one that was running is out of date
  (`settingsChanged`) the moment it's done.
- **What's kept.** Only the limits that differ from the engine's are written, so a limit never changed follows the
  engine's default if a later version moves it. A file edited by hand keeps the values that could be saved today,
  and the rest take the engine's.

### Data (E1: the readers that exist today)
'''),
]
