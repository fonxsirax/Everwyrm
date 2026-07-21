# From a Static Skybox to a Living Sky: an AI-Powered HDRP Engineering Journey

I spent literally the whole day on one single mission in **Everwyrm**, my dragon survival sandbox in Unity/HDRP: turning a static HDRI skybox — basically a photo glued to the world's sphere — into a full atmospheric system. Real day/night cycle, procedural night sky, a moon with phases, dynamic clouds, and water that reflects all of it. And honestly, the whole thing reminded me of something we tend to forget: **real-time visual quality doesn't come from one magic technique — it comes from a bunch of systems agreeing with each other.**

This is the honest version of that journey, bugs and all — including the ones that almost beat me.

*[IMAGE 1 — caption: "First attempt at night mode: pitch black everywhere, except the distant trees, which were somehow glowing like it was noon."]*

## Starting point: a sky that just wouldn't get dark

The plan looked simple on paper: a full day in 30 real-world minutes, a deterministic clock (`double` accumulating hours — so it never loses precision on long sessions) acting as the single source of truth for future weather, wildlife and gameplay systems. Rotate the sun, animate exposure and fog with curves, done.

Except rotating the sun did absolutely nothing to darken the world. The sky was a **static HDRI** — the lighting was literally baked into the texture. The real fix in HDRP is the **Physically Based Sky**: with it, the sun's actual position tints the atmosphere, the ambient light darkens on its own, and sunsets come "for free." I set the whole thing up in a runtime global Volume, with exposure and fog driven by curves exposed for the designer to tweak.

And then the first night arrived. It was a disaster.

## The broken-night saga

*[IMAGE 2 — caption: "Second attempt. Still dark, but now there's a suspicious band of glowing vegetation floating in the middle distance."]*

That screenshot sums it up perfectly: an unreadable void... with distant trees **glowing like daylight**. Every fix in the next rounds peeled back another layer of the same onion. Four causes, stacked on top of each other:

1. **Unlit billboards.** The distant LOD for trees used an HDRP/Unlit material with daylight literally *baked into the texture* — completely immune to any light source. At night, they turned into lanterns.
2. **The Transparent queue that survives a shader swap.** I fixed the billboard shader... and nothing changed. Turned out the material still carried a leftover `renderQueue = 3000` — and **transparent objects in HDRP don't receive shadows**. Swapping the shader doesn't reset the queue. Took me two hours to hunt down one invisible serialized field.
3. **Low-res moon shadows.** Bushes would "light up" from far away and go dark up close: the runtime moon light was using a default shadow map, and small objects were literally losing their own shadow at distance. Fixed by inheriting the shadow settings from the scene's calibrated sun.
4. **My own overcorrection.** Trying to kill leftover baked lighting, I'd zeroed out nighttime indirect lighting — and the foreground turned into a black pit, which made anything lit at all look "too bright" by contrast. The real fix: 0.3, not 0.02.

*[IMAGE 3 — caption: "Billboards fixed. What's left glowing here are actual white-blossom Maple trees — that's real color, not a bug."]*
*[IMAGE 4 — caption: "The bushes, ratting out the low-res moon shadow problem — bright far away, dark up close."]*

At that point I audited **all 74 materials** across every prefab in the procedurally generated world (forest, desert, tundra) with sweep scripts — and turned the whole process into idempotent fixes baked into the project's auto-setup: if a future asset shows up with the same problem, it fixes itself on the next recompile.

> **The prompt that made this happen:** *"You'd better run through all the assets that make up the procedurally generated world and see which materials need fixing, or find a more global solution."* That one line — asking for the systemic fix instead of the point patch — is what turned "fix that one bush" into a full sweep of all three biomes plus a permanent self-healing mechanism. Wide-scope prompt, architecture-level result.

*[IMAGE 5 — caption: "The night, finally coherent: dark, readable, no more glowing vegetation."]*

## The night sky: research before code

With the night stabilized, it was time for the part I actually wanted: a sky good enough to make you stop flying just to look up. Before writing a single line of code, I read through **HDRP 17's own source code** to squeeze the most out of the native pipeline — and the discovery that shaped the whole architecture was the PBS's `spaceEmissionTexture`: a cubemap rendered **behind the atmosphere**. That means stars disappear at dawn **through actual physics** (the lit atmosphere occludes them — no artificial fade needed), they automatically show up in reflections, and they rotate with `spaceRotation` (the sky itself rises and sets — at zero extra cost).

Stars are 100% procedural, generated in a shader straight into a cubemap using **the world's seed** — every world gets its own deterministic sky: three layers of stars with power-law brightness and black-body color temperature, plus a fBm-noise Milky Way band. Rare shooting stars (scheduled with a Poisson process — so they never fall into a predictable rhythm) rounded out the package.

The idea of tying the whole sky to the seed didn't come out of nowhere — it came from bouncing ideas off **Grok** about world identity in online games. In MMOs and multiplayer survival games, every server usually looks visually identical to the next one: same generated terrain, same sky. If the night sky is born from the seed, every world — every server, every save file — gets a **unique, permanent** constellation and Milky Way. That's not just eye candy, it's a real hook for collective immersion. Players sharing a world share a sky nobody else has — it becomes "our sky," not just "the game's sky."

And then I hit Play... **black sky. Nothing.**

## The deepest bug: a config flag

*[IMAGE 6 — caption: "Debug mode: forcing the raw cubemap as an HDRI proved the stars actually existed — the problem was somewhere in the PBS path, not the star generator."]*

Debugging turned into a proper funnel: a *probe* reading pixels straight off the generated cubemap (came back perfect — 42 HDR at the zenith), a debug toggle that displayed the raw cubemap as an HDRI (worked on the first try — proving the PBS path was the actual problem), an offline C# simulation of the shader math to sanity-check the generator, and reading the Editor log directly instead of relying on screenshots.

Worth being honest here about why this one bug ate the whole night — and it's really about what it's like to work with an AI on this kind of problem. **Claude**, my pair-programmer for this session, has no eyes on Play Mode. It can't see the game running, can't click anything, gets zero live visual feedback. Every single debugging step depended on me describing what I saw, sending screenshots, and the AI reading text logs from the Editor completely blind. That's a real limitation — a human dev would've popped open the Frame Debugger, inspected the shader live, or fired up RenderDoc to capture the frame and see exactly where the space texture stopped being sampled. The AI doesn't have that path available. In exchange, though, it can read **HDRP's entire source code** in seconds and cross-reference hundreds of lines of C# and HLSL hunting for the one compile-time flag silently killing a shader path — something that would've been an exhausting afternoon of grep for a human. It was that raw source-code reading, not visual intuition, that finally caught the flag. Back and forth between "I see the result" and "the AI reads the reason why" — each side covering the other's blind spot.

The root cause was hiding exactly where nobody thinks to look: the project had a **local copy of HDRP's config package** with `PrecomputedAtmosphericAttenuation = 1` — a flag that silently **disables the entire "space" rendering path** in the Physically Based Sky. One line, buried in a bundled package that came with the project template. Fixed the flag (in both files — the C# source and the generated HLSL have to match), recompiled shaders, and:

*[IMAGE 7 — caption: "The first stars ever rendered through the actual PBS atmosphere. Small win, huge relief."]*

## The moon that refused to show up

*[IMAGE 8 — caption: "Scene view: the star field is there, and you can even see the moon's specular reflection in the water — the light existed, the disc itself did not."]*

The protagonist was still missing. The moon's light was clearly working (you could even see its specular reflection on the water!), but the disc itself... nothing. Two more HDRP traps for the collection:

1. **`diameterMultiplerMode`**: with the default value, the renderer completely ignores the `angularDiameter` you set and falls back to a `diameterOverride` of 0.5°. My "big fantasy moon" at 7° was actually an invisible sliver.
2. **`AddHDLight`'s registration order**: HDRP's own helper method registers the light in its internal database *before* setting its type to Directional — and the classification into "celestial body" happens right at registration. Net result: the moon light never made it onto the celestial body list. Creating the `Light` component as Directional *before* adding the HD component fixed it. (This one deserves an actual bug report.)

While handling the technical side, I also worked on the art: the disc's surface texture. Used **ChatGPT** to generate a visual reference for the moon, with this prompt:

> *"Create an ultra-realistic ancient moon for a AAA sci-fi game. Preserve the recognizable lunar features but evolve them into a more visually striking celestial body. Add gigantic impact basins, fractured tectonic scars, subtle crystalline mineral formations, frozen lava rivers, faint iridescent metallic deposits, and variations in rock composition that create blue, silver, and slightly violet tones. The surface should remain believable and physically based, with no fantasy exaggeration. Every crater should contain rich micro-detail, weathering, dust accumulation, and erosion patterns. The moon should look spectacular when viewed from orbit yet remain highly detailed during close flybys. Cinematic quality, NASA-level realism combined with high-end science fiction aesthetics, optimized for HDRP/PBR rendering, 8K texture quality, seamless spherical projection, extremely sharp details, no atmosphere, no clouds, no artificial structures, no visible seams."*

That prompt is deliberately technical — PBR, seamless spherical projection, 8K — because the texture needed to behave like a production asset, not just a pretty illustration. It was going to become the `surfaceTexture` of an actual physical celestial body in HDRP, sampled through real orthographic projection; any lighting inconsistency or visible seam in the source image would've shown up as an artifact on the final disc. Asking for "cinematic, NASA-level realism... optimized for HDRP/PBR rendering" inside an image prompt is, in practice, writing an asset spec disguised as an art prompt.

*[IMAGE 9 — caption: "The moon finally appeared... as a blown-out white beacon. Progress, technically."]*
*[IMAGE 10 — caption: "The other side of that same sky, though, already hinted at the potential: blue dusk, stars scattered across it."]*

## Photometry: calibrating with numbers, not vibes

*[IMAGE 11 — caption: "Dragon under a moonlit, cloudy sky — gorgeous composition, except the moon looks like a solar eclipse."]*
*[IMAGE 12 — caption: "The astrophoto we picked as the disc's surface texture."]*
*[IMAGE 13 — caption: "Close-up of the actual problem: the glow ring completely swallowing the dark disc behind it."]*
*[IMAGE 14 — caption: "Desert night, calibration almost there: stars alive and well, moon still stuck in eclipse mode."]*

The moon went from blown-out to, later, with the real NASA texture (dark rock albedo, ~12% reflectivity), a full-on **"eclipse"** — a disc darker than the sky behind it, with the halo completely dominating. That's when I switched to the method that saved every calibration after this point: **calculating luminance in cd/m²** for every element against exposure, then defining target contrast ratios — sky ~4.6, clouds ~22 (5× the sky), average stars ~9 (2× the sky), halo ~104, disc ~250 (50× the sky, 2.4× the halo). Every "gut feeling" turned into an equation.

Somewhere in the middle of all this, one more classic error: **"No more space in the 2D Cookie Texture Atlas."** Turns out the moon's texture goes into HDRP's cookie atlas, has to be power-of-two, and the quality assets in use had their atlas capped at 512. The kind of detail no tutorial ever mentions.

*[IMAGE 15 — caption: "The final night: a moon with real craters and a soft glow, deep-blue sky, colorful stars, silver clouds. This is the one."]*

## The finish line: sky meets water

*[IMAGE 16 — caption: "The oasis at night, still too dark — Fresnel physics from a top-down angle shows the sandy bottom, not the sky."]*
*[IMAGE 17 — caption: "Water clarity dialed in: a proper depth gradient, deep petrol-blue body, moon specular on the surface."]*
*[IMAGE 18 — caption: "And in daylight: the full Fresnel effect in one shot — sky mirrored at grazing angle, clear water up close, golden sand visible through the shallows."]*

The last act was making the water actually **belong** to the sky. The foundation was already right — HDRP's native WaterSurface reflects the sky by design, with real Fresnel physics (mirror at grazing angles, transparent from above). But three things were quietly choking it: **SSR disabled** across the entire project, an inherited profile multiplying reflections by 0.7, and — me again — my own nighttime indirect-lighting dimmer cutting reflections down to 30% along with diffuse light. Splitting those two channels apart (diffuse follows the night curve; reflections always stay at 100%) is what finally reconnected the water to the world around it.

## What's left after all this

If I had to sum up the whole day in one sentence for other devs: **a good-looking sky isn't a feature — it's an agreement between about fifteen different systems.** The PBS won't shine if a config flag is quietly sabotaging it; stars won't show up if exposure doesn't respect them; the moon doesn't exist if the light database misclassified it at registration time; water won't reflect anything if two inherited multipliers are silently killing it. None of these pieces are individually hard. The actual work — and the actual quality — is in making them talk to each other.

Three practices that made the real difference: **reading the pipeline's source code before implementing anything** (every API I used was verified against the installed HDRP source, not generic docs); **debugging with instruments, not faith** (pixel probes, isolation toggles, offline simulation, logs); and **calibrating with photometry** (contrast ratios in cd/m² turn "this looks bad" into "the disc needs to be 50× brighter than the sky").

*(Built in pair-programming with AI — the four-handed investigation process, with the AI reading HDRP's source and the Editor logs while I visually validated every round, probably deserves its own article.)*

#gamedev #unity3d #hdrp #graphics #indiedev #madewithunity
