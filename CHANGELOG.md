# Changelog

## 1.0.1 (unreleased)

- Fixed: the stock Mk16 parachute kept its original lines as a rigid cone after the cut, and the canopy stayed a stiff
  dome. Its lines are drawn as a texture on a cone of wide triangles, which are now recognised as lines.
- The ropes take the colour of the original lines (read from the canopy texture, stock and ReStock). New setting
  `lineColorAuto`; `lineGray` is the fallback.
- The ropes start exactly where the original lines end at the hem, so the cut copy continues the lines without a jump.
- Stock Mk16: the ropes meet at the knot above the part like the original lines, with a riser down to the part, and
  are thinner to match the texture-drawn lines.
- The log lists the parts of a parachute that are still visible after the cut (helps with other parachute mods).

## 1.0.0 (2026-10-05)

First public release.

- Open canopies flutter, breathe and lean in the airflow; they keep their orientation at touchdown.
- Cut canopies are simulated as cloth with rope lines, wind, ground and water contact, stacked cloth layers,
  soaking and sinking.
- Multi-canopy parts (ReStock Mk16-XL) are split into separate canopies.
- Parallax terrain displacement is taken into account on land.
- Right-click entries to remove cut canopies; transparency under the cursor and in front of Kerbals.
- Translated menu texts (en, de, es, fr, it, pt-br, ru, ja, zh-cn).
- Offline tests for the Unity editor.
