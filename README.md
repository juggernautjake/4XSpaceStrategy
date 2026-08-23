# 4XSpaceStrategy

Real-time 4X Strategy Game inspired by Star Ruler 2 &amp; Stellaris

---

## Getting it running

### 1. Install Git LFS *before* you clone

**This is the step that breaks things if you skip it.** The ship and station meshes — 241 `.glb`
files, about 170 MB — are stored in [Git LFS](https://git-lfs.com). Clone without it and every one of
them arrives as a **three-line text file** instead of a model. The project still opens, and every ship
in the game is missing, which looks like a bug in the game rather than a bad checkout.

```
git lfs install
git clone https://github.com/juggernautjake/4XSpaceStrategy.git
```

**Already cloned without it?** Nothing is lost — fix it in place:

```
git lfs install
git lfs pull
```

To check it worked, open any file under `Assets/Resources/SpaceAssets/Ships/` in a text editor. If it
starts with `version https://git-lfs.github.com/spec/v1`, LFS has not fetched yet. A real model is
binary and starts with `glTF`.

### 2. Unity **6000.2.7f2**

That is the version in `ProjectSettings/ProjectVersion.txt`. Newer patch releases in the Unity 6.2
line will offer to upgrade the project and generally work; anything older will not open it. Packages
(glTFast, Input System, AI Navigation) restore themselves on first open — just let it finish.

### 3. Open it and press Play

Open `Assets/Scenes/SampleScene.unity` and hit Play.

**The scene is nearly empty on purpose.** Every manager, window and renderer in the game is created at
runtime by `GameBootstrap` (`Assets/Scripts/UI/GameBootstrap.cs`), which runs off
`[RuntimeInitializeOnLoadMethod]`. Nothing is wired up in the Inspector, so there is no scene setup to
get wrong and nothing to lose when the scene file is touched. If you are looking for where something
is built, it is in that file's list.

The first import takes a while — it is compiling ~230 scripts and importing 241 meshes.

### 4. A note on `.meta` files

Unity generates a `.meta` sidecar for every asset on first import, and most of this project's are not
yet committed, so you will see a few hundred of them appear as untracked changes.

**Leave them alone for now.** They should be committed once, from one machine, so that every checkout
shares the same asset GUIDs — and doing that from two machines at once produces exactly the collision
this note exists to avoid. See `CODEBASE_GUIDE.md` → Tooling for the whole story.

---

## Where things are

| | |
|---|---|
| **`CODEBASE_GUIDE.md`** | how the code is organised, and the tooling. Start here |
| `Assets/Scripts/` | all game code; nothing is Inspector-wired |
| `Assets/Scripts/UI/GameBootstrap.cs` | the list of everything the game creates at startup |
| `dev-requests/planning/` | what was asked for, what was built, and what is still open |
| `Art/README.md` | the ship-art pipeline: generating, importing, and looking at the result |
| `tools/` | the checkers and generators (Node + PowerShell, no build step) |

## Before you commit

There is no compiler in some of the environments this project is worked on from, so the repo carries
its own tripwires. Run this and get `Clean.`:

```
powershell -ExecutionPolicy Bypass -File tools/Check-Scripts.ps1
```

Seven checks — file heads, brace and paren balance, enum members, unterminated string literals, static
references, member accesses on resolvable locals, and whether a `foreach` source is actually iterable.
It is **not** a compiler and will not catch every type error, but it catches the classes that have
actually reached `main`. It needs `node` on your `PATH`; if that is missing it says so and fails
rather than reporting a pass it cannot back.
