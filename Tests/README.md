# ChuteFX offline tests

These tests run the real ChuteFX code in the Unity editor, without KSP. `generate.py` cuts the classes and functions
verbatim out of `Source/ChuteFX.cs` and pastes them into the templates in `templates/`; KSP types are replaced by
stand-ins. Nothing is reimplemented, so a test exercises exactly the code that ships.

## Requirements

- Unity **2019.4.18f1** (the version KSP 1.12 uses). Any project of that version works, an empty one is enough.
- Python 3.

## Running

```bash
export CHUTEFX_UNITY_PROJECT="/path/to/UnityProject"
export UNITY_EXE="C:/Program Files/Unity/Hub/Editor/2019.4.18f1/Editor/Unity.exe"
python run.py                 # all tests
python run.py ground          # one test
```

`run.py` regenerates the scripts in `<project>/Assets/Editor/ChuteFXTests/`, runs each test in batch mode and prints
its result lines. The full Unity logs and the images go to `<project>/ChuteFXTestOutput/`. To only regenerate the
scripts (for example to run a test from the editor), use `python generate.py <project>`.

## Tests

| Test | Method | Checks |
|------|--------|--------|
| island | `ChuteFXTest.IslandTest.Run` | Piece analysis (`StitchIslands`, `StitchLayers`) on a synthetic three-cap canopy like the ReStock Mk16-XL: each cap keeps its own apex piece, line-end caps and a far-away piece are removed. Prints `CHUTETEST result: OK`. |
| normal | `ChuteFXNormal.NormalTest.Run` | Render normals (`RenderNormals`) on a double-walled canopy whose skins meet in folds at the vent and the hem, with a UV seam: no normal stands across a fold and the seam is smooth. Naive averaging over both skins would be ~75° off, which shows up as a dark ring at the vent. |
| sample | `ChuteFXSample.SampleTest.Run` | Cluster samples (`DeformMesh.SampleWorld`) on a skinned dome after scaling and rotating the bones must match Unity's own skinning (`BakeMesh`) immediately. |
| ground | `ChuteFXGround.GroundTest.Run` | Three Mk16-XL caps and a large drogue are cut above flat ground and simulated for 30 s at 60 fps (`Step`, `LayerCloth`, `SmoothRender`, `RenderNormals`, `ClothMesh.Apply`). Logs per second whether each canopy is asleep, its fastest point and height; then run time per frame, interpenetration of cloth layers, and a top view that measures how much of the earlier canopy pokes through the later one. Renders images at 1.5–30 s, a close-up, smoothing levels 0/2/4 and the top views. |

Options of the ground test (environment variables):

- `CHUTE_WATER=1`: water landing (surface 1 m below the cut point, soaks for 20 s, then sinks; 60 s). `spike=` is
  the highest cloth point above the median cloth height, which catches cones rising in the middle of the canopy.
- `CHUTE_LAYER=<m>`: layer thickness, `0` turns the layer pass off.
- `CHUTE_SMOOTH=<n>`: render smoothing passes.
- `CHUTE_TAG`: prefix of the image names. `CHUTE_OUT`: output folder.

Reference values for v1.0.0: both canopies come to rest within 30 s (the XL on its own, the drogue through
`settleMax`); mean motion after 20 s is about 0.03 mm per frame; the earlier canopy shows through the later one on
well under 1 % of its area (about 20 % without layers); on water the spike settles at 0.25-0.5 m once the canopies
lie on the surface. Run times in the editor are from debug Mono and are much slower than in game.
