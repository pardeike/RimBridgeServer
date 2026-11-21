# Optional Waylith development tools

Development-only companion for RimWorld 1.7.4981 / Waylith .NET10. It is excluded
from the player mod ZIP and disabled in normal builds. Tools are discovered under
`waylith/`: status, profile_capture, development_tests and shader_proof.

Build and run project tests with the installed 1.7 managed directory:

```sh
scripts/build-mod.sh --test -- -p:BuildWaylithTools=true -p:RimWorld17ManagedDir='/absolute/RimWorld/RimWorld.app/Contents/MacOS'
```

The output is `artifacts/BridgeTools/Waylith/Waylith.BridgeTools.dll`. An explicit
opt-in deployment through `scripts/deploy-local-mod.sh --mods-dir PATH --` with
those same properties copies it to the Mods folder's lexical sibling
`BridgeTools/Waylith`. Stop the owned game before replacing a companion. Do not
ship this development companion or install it into another agent's live session.

For an isolated, self-cleaning live validation use the existing command:

```sh
scripts/live-smoke.sh --waylith-poc --game-root '/absolute/RimWorld' --gabs-bin /absolute/path/to/gabs
```

The companion restores SDK 2.0.0 from NuGet.org. The live fixture uses the SDK
shipped by the installed 1.7 host and verifies the loaded Workshop Harmony root.
`shader_proof` needs the temporary fixture mod;
it is not a general shader editor. `development_tests` accepts a baseline.bin
and ordered generation-N.metadata/.il/.pdb with manifest.json baselineSha256.
Only synchronous zero-argument public static annotated tests on sealed types
are supported. Its registry is separate from the ordinary startup MCP registry.
The profiler captures scalar frame information, preserving existing flags;
it does not instrument arbitrary mod methods.

Read [the proven workflow and limits](../../skills/rimworld-17/references/autonomous-development.md)
before applying these techniques. The tests are development iteration aids;
restart and validate the final built mod before calling a change accepted.
