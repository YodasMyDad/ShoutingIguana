//! The tray icon with a progress ring: the app's logo, scaled into the middle, inside a ring that fills clockwise
//! from twelve o'clock. Drawn in RGBA at runtime, anti-aliased by supersampling.

use std::f32::consts::TAU;

/// The icon's width and height. Windows scales it to the tray's size for the display's DPI.
pub const SIZE: usize = 32;

/// The logo's width and height inside the ring.
const LOGO: usize = 22;
const OUTER_RADIUS: f32 = 15.75;
const INNER_RADIUS: f32 = 12.5;
/// Samples per pixel along each axis.
const SAMPLES: usize = 4;

/// Straight RGB from 0 to 1, and an alpha.
type Colour = ([f32; 3], f32);

/// The brand's blue, from the logo.
const ACTIVE: Colour = ([0x2b as f32 / 255.0, 0x8f as f32 / 255.0, 0xff as f32 / 255.0], 1.0);
const PAUSED: Colour = ([0x9a as f32 / 255.0, 0xa0 as f32 / 255.0, 0xa6 as f32 / 255.0], 1.0);
/// The unfilled part, visible on light and dark taskbars alike.
const TRACK: Colour = ([0.5, 0.5, 0.5], 0.4);

/// `logo` is square RGBA, `logo_size` pixels wide. Returns `SIZE` × `SIZE` RGBA.
pub fn draw(logo: &[u8], logo_size: usize, percent: u8, paused: bool) -> Vec<u8> {
    // Premultiplied RGBA while drawing.
    let mut canvas = vec![[0.0f32; 4]; SIZE * SIZE];
    let offset = (SIZE - LOGO) / 2;
    for (index, pixel) in resample(logo, logo_size, LOGO).into_iter().enumerate() {
        canvas[(offset + index / LOGO) * SIZE + offset + index % LOGO] = pixel;
    }

    let sweep = f32::from(percent.min(100)) / 100.0 * TAU;
    let fill = if paused { PAUSED } else { ACTIVE };
    let centre = SIZE as f32 / 2.0;
    let per_sample = 1.0 / (SAMPLES * SAMPLES) as f32;
    for (index, pixel) in canvas.iter_mut().enumerate() {
        let (x, y) = ((index % SIZE) as f32, (index / SIZE) as f32);
        let (mut track, mut filled) = (0.0, 0.0);
        for sample in 0..SAMPLES * SAMPLES {
            let dx = x + ((sample % SAMPLES) as f32 + 0.5) / SAMPLES as f32 - centre;
            let dy = y + ((sample / SAMPLES) as f32 + 0.5) / SAMPLES as f32 - centre;
            let radius = dx.hypot(dy);
            if !(INNER_RADIUS..=OUTER_RADIUS).contains(&radius) {
                continue;
            }
            // Clockwise from twelve o'clock, with y growing downwards.
            if dx.atan2(-dy).rem_euclid(TAU) < sweep {
                filled += per_sample;
            } else {
                track += per_sample;
            }
        }
        over(pixel, TRACK, track);
        over(pixel, fill, filled);
    }

    canvas
        .into_iter()
        .flat_map(|[red, green, blue, alpha]| {
            let straight = |channel: f32| if alpha > 0.0 { channel / alpha } else { 0.0 };
            [straight(red), straight(green), straight(blue), alpha]
                .map(|channel| (channel.clamp(0.0, 1.0) * 255.0).round() as u8)
        })
        .collect()
}

/// Composites `colour` at `coverage` over a premultiplied pixel.
fn over(pixel: &mut [f32; 4], (rgb, alpha): Colour, coverage: f32) {
    let alpha = alpha * coverage;
    if alpha <= 0.0 {
        return;
    }
    for channel in 0..3 {
        pixel[channel] = rgb[channel] * alpha + pixel[channel] * (1.0 - alpha);
    }
    pixel[3] = alpha + pixel[3] * (1.0 - alpha);
}

/// Scales square RGBA down by averaging the area each target pixel covers. Returns premultiplied pixels.
fn resample(source: &[u8], source_size: usize, size: usize) -> Vec<[f32; 4]> {
    let scale = source_size as f32 / size as f32;
    let overlap = |index: usize, from: f32, to: f32| (to.min(index as f32 + 1.0) - from.max(index as f32)).max(0.0);
    let mut pixels = Vec::with_capacity(size * size);
    for target_y in 0..size {
        let (top, bottom) = (target_y as f32 * scale, (target_y + 1) as f32 * scale);
        for target_x in 0..size {
            let (left, right) = (target_x as f32 * scale, (target_x + 1) as f32 * scale);
            let mut sum = [0.0f32; 4];
            let mut area = 0.0;
            for source_y in top.floor() as usize..(bottom.ceil() as usize).min(source_size) {
                let weight_y = overlap(source_y, top, bottom);
                for source_x in left.floor() as usize..(right.ceil() as usize).min(source_size) {
                    let weight = weight_y * overlap(source_x, left, right);
                    let at = (source_y * source_size + source_x) * 4;
                    let Some(&[red, green, blue, alpha]) =
                        source.get(at..at + 4).and_then(|pixel| <&[u8; 4]>::try_from(pixel).ok())
                    else {
                        continue;
                    };
                    let alpha = f32::from(alpha) / 255.0;
                    for (channel, value) in [red, green, blue].into_iter().enumerate() {
                        sum[channel] += f32::from(value) / 255.0 * alpha * weight;
                    }
                    sum[3] += alpha * weight;
                    area += weight;
                }
            }
            pixels.push(if area > 0.0 { sum.map(|channel| channel / area) } else { [0.0; 4] });
        }
    }
    pixels
}

#[cfg(test)]
mod tests {
    use super::*;

    fn solid(size: usize, rgba: [u8; 4]) -> Vec<u8> {
        rgba.repeat(size * size)
    }

    fn pixel(icon: &[u8], x: usize, y: usize) -> [u8; 4] {
        let at = (y * SIZE + x) * 4;
        [icon[at], icon[at + 1], icon[at + 2], icon[at + 3]]
    }

    fn is_blue(rgba: [u8; 4]) -> bool {
        rgba[3] > 200 && rgba[2] > 200 && rgba[0] < 100
    }

    fn is_grey(rgba: [u8; 4]) -> bool {
        rgba[3] > 200 && rgba[0].abs_diff(rgba[2]) < 20 && rgba[0] > 120
    }

    #[test]
    fn the_icon_is_32_by_32_rgba() {
        assert_eq!(draw(&solid(64, [0, 0, 0, 0]), 64, 50, false).len(), SIZE * SIZE * 4);
    }

    #[test]
    fn the_ring_fills_clockwise_from_the_top() {
        let logo = solid(64, [0, 0, 0, 0]);
        let half = draw(&logo, 64, 50, false);
        // Just right of twelve o'clock, and three o'clock: filled. Nine o'clock: the track.
        assert!(is_blue(pixel(&half, 17, 1)), "{:?}", pixel(&half, 17, 1));
        assert!(is_blue(pixel(&half, 30, 16)), "{:?}", pixel(&half, 30, 16));
        let track = pixel(&half, 1, 16);
        assert!(!is_blue(track) && track[3] > 60 && track[3] < 160, "{track:?}");

        let none = draw(&logo, 64, 0, false);
        assert!(!is_blue(pixel(&none, 17, 1)));
        let full = draw(&logo, 64, 100, false);
        assert!(is_blue(pixel(&full, 1, 16)) && is_blue(pixel(&full, 15, 1)));
    }

    #[test]
    fn a_paused_ring_is_grey() {
        let paused = draw(&solid(64, [0, 0, 0, 0]), 64, 50, true);
        assert!(is_grey(pixel(&paused, 30, 16)), "{:?}", pixel(&paused, 30, 16));
    }

    #[test]
    fn the_logo_sits_in_the_middle_and_the_corners_stay_clear() {
        let icon = draw(&solid(64, [200, 40, 40, 255]), 64, 0, false);
        assert_eq!(pixel(&icon, 16, 16), [200, 40, 40, 255]);
        assert_eq!(pixel(&icon, 0, 0), [0, 0, 0, 0]);
        assert_eq!(pixel(&icon, 31, 31), [0, 0, 0, 0]);
        // Outside the logo but inside the ring: nothing.
        assert_eq!(pixel(&icon, 4, 16)[3], 0);
    }

    #[test]
    fn scaling_down_averages_without_darkening_transparent_edges() {
        let mut checker = Vec::new();
        for index in 0..4 * 4 {
            checker.extend_from_slice(if index % 2 == 0 { &[255, 255, 255, 255] } else { &[0, 0, 0, 0] });
        }
        let scaled = resample(&checker, 4, 2);
        for [red, _, _, alpha] in scaled {
            assert!((alpha - 0.5).abs() < 1e-6);
            // Premultiplied white at half alpha: straight white, not grey.
            assert!((red / alpha - 1.0).abs() < 1e-6);
        }
    }
}
