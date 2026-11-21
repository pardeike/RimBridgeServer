# Waylith developer notes

Snapshot: 2026-10-17. These are dated statements by the Waylith developer and
the Modder Primer, not fresh runtime tests. Discord links point to the Waylith
Discord. Resolve differences against the actual installed build.

## Runtime hooks and faster iteration

- **Release Edit and Continue.** The developer says WaylithCLR enables Edit and Continue
  even with the game built in Release, allowing mod code edits in that process.
  This requires a compatible debugger/edit workflow; copying a rebuilt DLL over
  a loaded assembly is not established as hot reload. Verify the specific edit
  and restart for final acceptance. [September 30 discussion](https://discord.com/channels/1551763520004169888/1551763521052868740/1554932374406561946).
- **Runtime IL replacement and re-JIT.** The custom runtime exposes method IL
  get/set and explicit re-JIT, introduced for instrumentation and possible use
  by patching mods. The developer identifies `WaylithClr.MethodBodyUpdater` in
  `System.Private.CoreLib`, correcting an earlier `System.Runtime.dll`
  attribution in the same conversation. Inspect its current contracts with DecompilerServer
  before integration, including interaction with Harmony and instrumentation.
  This is not a stock .NET API or proof that Prepatcher works.
  [September 29 corrected API location](https://discord.com/channels/1551763520004169888/1552481626829758474/1554505598576500747),
  [October 1 intent](https://discord.com/channels/1551763520004169888/1551763521052868740/1555238490151723040),
  [October 13 clarification](https://discord.com/channels/1551763520004169888/1551763521052868740/1559602386538995814).
- **Build identity.** The developer says assemblies now carry commit hashes and
  `RimWorld.dll` has the game version. Record these along with file hashes when
  diagnosing a build. [October 16 discussion](https://discord.com/channels/1551763520004169888/1551763521052868740/1560741876632522954).

## Profiling a mod's actual cost

The assembly picker has separate choices for an entire assembly and its Harmony
patch methods. The developer recommends enabling instrumentation, running the actual
scenario at max speed, collecting a full graph, selecting expensive frames, then
examining wide scopes. This does not automatically account for all downstream
work triggered by a patch, and timing a transpiler's startup execution is a
different question from timing the resulting patched method.
[Picker explanation and capture workflow, October 8](https://discord.com/channels/1551763520004169888/1551763521052868740/1557673521193287763).

Keep engine/game context when needed; profiling only a mod can conceal where its
work occurs in a frame. The primer documents allocation and VRAM views, GC
markers, all-thread collection, and instrumentation filters. Auto-mute excludes
very short scopes to limit distortion; disabling it adds overhead. The profiler's
FPS target changes severity colors, not the game's framerate.
[Developer on context](https://discord.com/channels/1551763520004169888/1552351566659321967/1555601515883008104),
[modder primer](https://docs.google.com/document/d/1PtuKCNbh1z_hlkcW9T4xbdHVRjinx8TdIVmEGg6yook/edit).

## Jobs and native rendering

The primer describes `Waylith.Jobs.WaylithJobRuntime` as a job scheduler that
accepts C# methods, including per-item parallel work and job dependencies.
`Schedule*` returns an integer job ID. `Complete(id)` waits and can steal work
onto the calling thread. Non-Schedule counterparts schedule then complete, so
they block. Inspect current overloads and keep mutable game-state access safe;
availability of worker jobs does not make arbitrary Verse operations thread-safe.

For native render features the developer describes `CaptureFrame` on the main thread
creating a context object, then render passes consuming that object on the
render thread. Capture the state needed for the frame; preserve resource/data
lifetime until GPU use completes. `FrameRenderer` supports adding, removing,
inserting and querying features. Inspect ordering and disposal contracts before
writing a feature. [Capture/render boundary, September 26](https://discord.com/channels/1551763520004169888/1551763521052868740/1553420989151576077),
[render-feature APIs, build 4959](https://discord.com/channels/1551763520004169888/1552470960836313209/1553145791575621694).

The developer also reports native compute facilities and CPU mesh skinning. That does not
establish Unity ComputeShader or Animator compatibility. Build 4971 explicitly
adds meshes/prefabs from bundles without Animator support.
[Compute compatibility limit](https://discord.com/channels/1551763520004169888/1552481626829758474/1554068909898997823),
[CPU skinning, October 14](https://discord.com/channels/1551763520004169888/1551763521052868740/1560004291983310945),
[build 4971](https://discord.com/channels/1551763520004169888/1552470960836313209/1557349623213985836).

## Shader iteration and porting traps

`-WatchShaders` reloads mod shader sources; in-game hot-reload-defs supports
ShaderDef changes. Mod shaders can override vanilla, and shader roots are
registered before mod constructors. Invalid shaders render magenta even if they
compile. [Build 4962](https://discord.com/channels/1551763520004169888/1552470960836313209/1554221823070306407),
[build 4970](https://discord.com/channels/1551763520004169888/1552470960836313209/1557055783064305848).

The primer is the detailed porting reference. It documents these easy mistakes:

- GLSL sources go in a load folder's `Shaders/`, split into vertex/fragment files
  with `#version 450`; ShaderDef/MaterialDef replace ShaderLab metadata.
- `_ST.xy` is offset and `_ST.zw` is scale, the reverse of Unity's layout.
  Texture UVs also differ; follow the primer's flip rules, not a universal flip.
- The fragment shader alone defines material layout, including fields required
  only by the vertex stage. Instanced fragment shaders must include the transform
  declarations even when they do not read them.
- Set 1 is reserved. Set 2 binding 0 contains material uniforms; instancing uses
  bindings 2 and 3, so place custom SSBOs at 4 or above when combined with it.
- A material uniform with `@default` does not read a shader global. Names must
  match C# properties. macOS has a 16-sampler-per-stage constraint in this path.
- Reuse an existing scene grab when possible. Grab passes are costly, and the
  material must render after the grab it consumes. A render-queue mistake can
  produce black output even if older behavior happened to retain usable pixels.

[Primer, last modified October 13](https://docs.google.com/document/d/1PtuKCNbh1z_hlkcW9T4xbdHVRjinx8TdIVmEGg6yook/edit),
[Developer's render-queue diagnosis, October 13](https://discord.com/channels/1551763520004169888/1551763521052868740/1559618864076296295).

## Assets, startup and outdated advice

- Bake with `-BakeModAssetPacks=ABSOLUTE_MOD_ROOT`. Paths are filesystem roots,
  not package IDs. Relative paths resolve against the working directory. Use
  this spelling; one October 8 chat example has an extra `s` in `AssetsPacks`.
- `-Headless` bakes with CPU encoding without a GPU; `-NoWindow` and visible
  execution use GPU encoding. Output includes textures/audio only, one
  `Assets.zip` per applicable load folder. These ZIPs use method 93, Zstandard.
  Ordinary ZIP software may not support it.
- the developer says baking traverses 1.7-or-newer load folders even for conditional mod
  requirements that are not currently active. Inspect generated files in a
  staging copy. `-Verbose` logs `Asset pack miss` for assets loaded loose; a miss
  alone is not a broken release because loose assets can be intentional.
  [Build 4972](https://discord.com/channels/1551763520004169888/1552470960836313209/1557706100294418453),
  [folder selection](https://discord.com/channels/1551763520004169888/1551763521052868740/1557834830899843244),
  [miss diagnostics](https://discord.com/channels/1551763520004169888/1551763521052868740/1557835577116721243).
- Per-mod caching, parallel texture/audio loading and LZMA-to-LZ4 bundle caches
  changed startup substantially in build 4966. The developer recommended testing without
  Faster Game Loading, Missilegirl's XML caching, Loading Progress and the older
  texture-cache mod. He observed per-state frame yields in Loading Progress
  costing time. Compare wall-clock runs on an isolated profile before deciding
  whether to remove any such mod; do not change the player's active list.
  [Build 4966](https://discord.com/channels/1551763520004169888/1552470960836313209/1555362460012388465),
  [yield overhead](https://discord.com/channels/1551763520004169888/1551763521052868740/1555268854878310524),
  [October 1 recommendation](https://discord.com/channels/1551763520004169888/1551763521052868740/1555363995702005841).
- the developer's one recorded `HARMONY_COMPILATION_WORKERS=4` comparison showed no material
  improvement on his mod list. It is not evidence that overlap never helps or a
  reason to set that variable as a generic preset.
  [October 2 measurement](https://discord.com/channels/1551763520004169888/1551763521052868740/1555536488714731534).
- Video support landed in build 4967, explicitly including Achtung's tutorial.
  FMOD was removed in build 4973 in favor of miniaudio. Earlier `-fmod` advice is
  obsolete. Early September posts listing particles, GUILayout and video as
  unsupported are also superseded by later builds.
  [Video](https://discord.com/channels/1551763520004169888/1552470960836313209/1555721475926003865),
  [FMOD removal](https://discord.com/channels/1551763520004169888/1552470960836313209/1558138611290083369).
