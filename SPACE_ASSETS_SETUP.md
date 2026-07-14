# 🌌 Space Assets & Effects — Setup Guide

This project already looks good out of the box: planets, nebulae, starfields and post-processing are
**generated procedurally in code** — nothing to download. This guide is for the *optional* extra
polish: free, legally-safe (CC0 / permissive) art packs and add-ons that plug into what we built.

> **TL;DR**
> - **Already automatic (no download):** URP Bloom/Vignette/ACES post-processing, procedural nebula
>   background, procedural planet textures & atmospheres. See `PostFxController` and `SpaceBackground`.
> - **Auto-*used* if you drop them in:** any textures placed in `Assets/Resources/SpaceAssets/…`
>   are detected and applied automatically (see [Drop-in folder](#3-drop-in-folder-auto-detected)).
> - **Cannot be auto-downloaded:** Unity **Asset Store** packs (they require a licensed, logged-in
>   import). Those have quick manual steps below.

---

## Why "download during build" is only partly possible

- **UPM / OpenUPM packages** (code, shaders, tools) *can* resolve automatically — Unity reads
  `Packages/manifest.json` and downloads them on project open / build. See [Section 4](#4-auto-installing-upm-packages).
- **Asset Store art** (textures, skyboxes, models) **cannot** be scripted to download. The Asset
  Store requires a logged-in Unity account and a licensed import; there is no legal/technical way to
  fetch them headlessly during a build. For those we use a **drop-in folder** so that once you import
  them *once*, the game wires them up automatically.

---

## 1. Recommended FREE (CC0 / public-domain) art

All of these are **CC0 / public domain** — free for commercial use, no attribution required. Verify
the license on the page before shipping.

| Asset | What it's for | Source |
|---|---|---|
| **Planet Surface Textures** (Screaming Brain Studios) — 150 textures | Detail maps on the 3D planets | https://screamingbrainstudios.itch.io/planet-texture-pack-1 |
| **Seamless Space Backgrounds** (Screaming Brain Studios) | Alternative flat/skybox backdrops | https://screamingbrainstudios.itch.io/seamless-space-backgrounds |
| **Solar System Textures** (Solar System Scope) — real planet maps | High-res planet/atmosphere detail | https://www.solarsystemscope.com/textures/ |
| **Jettelly Space Skyboxes** (6 skyboxes, CC0) | Cubemap/skybox backdrop | https://jettelly.com/blog/some-space-skyboxes-why-not |
| **3DTextures.me** (CC0 PBR) | Rock/ice/sand detail + normal maps | https://3dtextures.me/tag/cc0/ |

Optional, free but **Asset Store** (manual import, still free):

| Asset | Use | Link |
|---|---|---|
| **SpaceSkies Free** (Pulsar Bytes) | Ready skyboxes | https://assetstore.unity.com/packages/2d/textures-materials/sky/spaceskies-free-80503 |
| **FREE Skybox Extended Shader** (Boxophobic) | URP-ready animated skybox shader | https://assetstore.unity.com/packages/vfx/shaders/free-skybox-extended-shader-107400 |

---

## 2. What's already integrated (zero setup)

- **`PostFxController.cs`** — on play, enables URP **Bloom, Vignette, ACES tonemapping, color
  grading** in code. Makes the star, atmospheres and orbit rings glow while keeping the foreground
  readable. No package download; URP 17.2 is already in this project.
- **`SpaceBackground.cs`** — generates a per-map **nebula + starfield** with parallax, twinkling
  stars, shooting stars and distant galaxies. Toggle / recolor / regenerate from the **Background**
  button in the top HUD bar.

---

## 3. Drop-in folder (auto-detected)

Create this folder (exact name/casing) and the game uses whatever you put in it, falling back to the
procedural look when it's empty:

```
Assets/
  Resources/
    SpaceAssets/
      Detail/
        RockyPlanet.png            ← detail albedo for rocky worlds (tiling)
        RockyPlanet_normal.png     ← detail normal map (set Texture Type = Normal map)
        OceanPlanet.png
        IcePlanet.png
        VolcanicPlanet.png
        GasGiant.png
        BarrenPlanet.png
        Moon.png
      Skybox.mat                   ← optional skybox Material (see below)
```

- **Detail maps** (`Detail/<BodyType>.png` and `<BodyType>_normal.png`) are layered onto the 3D
  planets automatically by `AssetIntegration.ApplyPlanetDetail`. The planet's *base* map stays
  procedural on purpose, so the globe keeps matching the 2D map view — the detail pack just adds
  fine surface texture. Names must match the `CelestialBodyType` enum exactly.
- **Skybox** (`SpaceAssets/Skybox.mat`): if present, it's registered as `RenderSettings.skybox`.
  To actually show it, set the **Main Camera → Clear Flags = Skybox** (and optionally toggle off the
  procedural background from the HUD).

> After copying files in, let Unity import them, then press Play. No code changes needed.

### Turning a CC0 planet texture into a detail map
1. Import the `.png` into `Assets/Resources/SpaceAssets/Detail/` and rename to the body type.
2. For `_normal` maps, select the texture → Inspector → **Texture Type = Normal map** → Apply.
3. Set **Wrap Mode = Repeat** so it tiles.

---

## 4. Auto-installing UPM packages

For *code/shader* add-ons (not art), you can make Unity download them automatically. Add an OpenUPM
scoped registry to `Packages/manifest.json`. Example (Keijiro's popular free VFX utilities):

```jsonc
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [ "jp.keijiro" ]
    }
  ],
  "dependencies": {
    "jp.keijiro.metamesh": "1.0.4"
    // add more "scope.package": "version" lines here
  }
}
```

On next project open, Unity's Package Manager resolves and downloads these **automatically** — this is
the closest thing to "download during build." Browse packages at https://openupm.com.

> Post-processing itself needs **no** package here — URP (already installed) includes it, and
> `PostFxController` turns it on for you.

---

## 5. Quick checklist

- [ ] Press Play — you should already see bloom, a nebula backdrop and textured planets.
- [ ] (Optional) Download a CC0 pack from Section 1.
- [ ] (Optional) Drop textures into `Assets/Resources/SpaceAssets/Detail/` using the exact body-type names.
- [ ] (Optional) Add a `Skybox.mat` and set Camera Clear Flags = Skybox.
- [ ] (Optional) Add OpenUPM packages via `manifest.json` for auto-download.

Everything degrades gracefully: if you skip all of it, the game still looks complete.

_Sources: Screaming Brain Studios (itch.io), Solar System Scope, Jettelly, 3DTextures.me, OpenUPM docs, Unity Asset Store._
