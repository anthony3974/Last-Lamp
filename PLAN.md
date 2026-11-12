# Last Lamp: Plan

**When asked "what's next?", the answer is the first open item under NEXT UP** (order set by the owner).
**To continue in a new chat:** "Read PLAN.md and start the NEXT UP task."

Unity 6000.0.85f1, Universal Render Pipeline (URP).

## NEXT UP (in order)

Goal of this list: one graybox corridor, playable on PC, that you can cross by stealth alone. Each task is small enough for one session and ends with a check. Tick it off when the check passes.

Unity locks the project while the editor is open, so change Project Settings through a one-off Tools menu script (see Rules) rather than by editing the files on disk.

### Project setup
- [x] **1. Identity and platform.** Company `Tony3974`, product `Last Lamp`, package `com.Tony3974.LastLamp` (matches Farming-Simulator-94). Build for Linux on this machine; Windows build support is already installed for when the game is shared. *Check: an empty PC build opens and runs.*
- [x] **2. Clear out the template.** Delete `Assets/TutorialInfo` and `Assets/Readme.asset`. Remove the Visual Scripting, Multiplayer Center and Version Control packages. *Check: no console errors after Unity reloads.*
- [x] **3. Project structure.** Folders under `Assets`: `Scenes`, `Scripts/{Player,Stealth,Enemies,UI}`, `Scripts/Tests`, `Prefabs`, `Materials`, `Editor`. One `LastLamp` assembly for the game and one `LastLamp.Tests` for edit mode tests. Layers: `Player`, `Enemy`, `VisionBlocker`, `Interactable`. Rename `SampleScene` to `Corridor_01`. *Check: one empty edit mode test passes.*
- [x] **4. Input and camera packages.** Add Cinemachine 3. In `InputSystem_Actions`, add `Dodge` (right mouse, right bumper) and remove `Previous` and `Next`. Keep `Jump` on Space. On keyboard, `Crouch` is Left Shift and `Sprint` is Q (owner, 2026-11-08). *Check: all remaining actions have keyboard and gamepad bindings, and the project compiles.*

### Prove the look (the game depends on it, so it comes first)
- [x] **5. Noir lighting test.** Build a graybox corridor with six lamps. Use real-time Spot lights with shadows, which the PC profile already supports, so the player and enemies throw shadows and lamps can flicker or go out. Use a volume for muted color (Saturation -45, so the brown crates still show), high contrast and vignette. Added at the owner's request: an open yard at each end of the corridor with a night cloud sky (tune it on the `Night_Sky` material). *Check: on PC the shadows are hard and clear, a moving capsule throws a shadow, the pools read clearly, and it holds 60 fps.*

### Core loop in graybox
- [x] **6. Player.** Third-person walk, crouch, sprint and jump with a Cinemachine follow camera. Keyboard and mouse, plus gamepad. *Check: the corridor can be walked end to end with both.* Passed on keyboard and mouse. Gamepad not tested yet (owner skipped it for now, 2026-11-08).
- [x] **7. Light zones and the visibility cue.** Each lamp has a `LampZone` with a radius that marks its pool. Gameplay reads these zones and never the rendered light, so tuning the look can't change the rules. Visibility is one 0 to 1 value, shown on screen. Inside a pool it is 0. In the dark, moving raises it, crouching lowers it, and landing from a jump spikes it. *Check: the cue changes clearly when entering a pool, crouching and running.*
- [x] **8. Noise.** Footsteps (loudest when sprinting, silent when crouched), landing from a jump and knocked-over objects emit a noise event with a position and radius. Add placeholder footstep audio. *Check: a debug gizmo shows the radius for each noise.*
- [x] **9. One enemy.** NavMesh patrol, vision cone blocked by `VisionBlocker`, and hearing from task 8. Each lit pool is a carving NavMesh Obstacle, so enemies stay out of it and a lamp going out opens the path. An enemy chasing the player to a pool waits at the edge for a short time, then goes to Search, so the player can't be trapped there forever. States: Patrol, Suspicious (the warning beat), Investigate, Chase, Search, back to Patrol. Keep the detection maths in plain C# so it can be tested. *Check: edit mode tests prove detection can never go from unseen to fully spotted without passing through Suspicious, the enemy gives a visible and audible warning in play, and it stops at the edge of a pool and later gives up.*
- [x] **10. Hiding spot.** Interact to enter and leave. Entering unseen ends a chase: the enemy carries on to where it lost the player, then searches that area for longer than usual (owner, 2026-11-09). *Check: a chase can be escaped by hiding, and hiding in full view of the enemy does not work.*
- [x] **11. Getting caught.** Being caught costs something and returns the player to a checkpoint, with no scripted capture anywhere. Add one punch and one dodge against the one enemy type. *Check: caught, fought and escaped outcomes all play out without getting stuck.*
- [x] **12. Finish the corridor and playtest.** Add an end point and restart. Lay out lamps, two monsters with a route each (owner, 2026-11-09) and two hiding spots. *Check: in a PC build, the corridor can be crossed without being spotted, and each Design Goal below has been tried and noted as working or not.*

Playtest result (owner, 2026-11-09): all good, and two monsters was the step the corridor needed.

### Warehouse
- [x] **13. Make the way through readable (owner, 2026-11-09).** In `Warehouse_01`, highlight the exit door (the blocked back door, owner 2026-11-09), the path routes and the stairs, or turn up the overall brightness if that reads better. Highlights must not look like lit pools, because only lit pools are safe. *Check: a first-time player can find the stairs and the exit without help, and can still tell safe light from dark.*
  - Built 2026-11-10 (`6fc53c0`, by a one-off **Tools > Add Way Highlights**, now only in git history): the top bar of every stair and catwalk rail glows faint cyan (`Highlight_Rail`), and the back door has a faint green outline (`Door_Outline_*` on `Highlight_Exit`). Both are unlit, below bloom, with no lights, colliders or shadows. Passed by the owner, 2026-11-11.
- [x] **14. Opening front door (owner, 2026-11-09).** The front door of `Warehouse_01` is only a gap in the wall for now. Give it a real door that opens. Monsters still must not follow the player outside, and locking the door behind the player stays the fallback if they do. *Check: the door opens and the player can go in and out, and a chasing monster stops at the doorway.*
  - Passed by the owner, 2026-11-11. Built 2026-11-11 by **Tools > Build Front Door** (`FrontDoorBuilder`, now only in git history). An old wooden double door, `Front_Door` under `Warehouse`: five weathered planks a leaf, black iron strap hinges and a ring pull outside, ledges and braces inside, in a wooden frame. The wood texture and meshes are generated by the tool into `Assets/Art/Door`, with `Door_Wood` and `Door_Iron` materials. Interact (E) opens and closes it, and the prompt says "E  Open" or "E  Close" (`PlayerDoors`, `InteractPrompt`). The leaves swing away from the player, so they never hit them, and closing waits until the player is clear of the leaves. It creaks (`Door_Creak`, placeholder) and thuds shut (`Door_Shut`), and monsters can't hear it (owner, 2026-11-11: `noiseRadius` on `Door` is 0; raise it to make the creak a noise they hear). A closed door blocks monster sight, so shutting it on a chase makes the monster give up after 2.5 s.
  - Bug found and fixed: `Door_No_Monsters` sat 0.5 m above the floor (`WarehouseBuilder` never set its centre), so it never marked anything, and the NavMesh ran out through the doorway onto the street lamp's lit pool, about 1 m outside the wall. Its centre is lowered to reach below the floor and the warehouse NavMesh is rebaked. Monsters now stop at Z 1.8, about 2 m inside the door line, so they can't catch a player standing in the doorway or outside. Every patrol route still joins up outside the light.

- [x] **15. Make the game brighter (owner, 2026-11-10).** The game is too dark overall. Raise the base brightness (ambient light, volume exposure, the dim flashlight) without losing the noir look, and without lit pools stopping reading as the only safe light. Task 13 covers highlighting the way through in `Warehouse_01`; this one is the game-wide level, and the two can share the same pass. *Check: the corridor and the warehouse are easy to read on a normal monitor, the pools are still clearly the safe spots, and the stealth rules are unchanged.*
  - Done 2026-11-10 (`c3e7c0a`), ticked by the owner 2026-11-11: ambient light 0.05 to 0.3, post exposure +1 in `SampleSceneProfile`, and the flashlight's intensity 4 to 12 and range 9 to 13, in both levels. The stealth rules are unchanged, because gameplay reads the `LampZone`s, not the rendered light.

- [x] **17. "This door is blocked" text at the back door (owner, 2026-11-10).** When the player gets close to the blocked back door of `Warehouse_01`, show a short on-screen text box: "This door is blocked." It hides again when they walk away. Plain positioned UI, like the rest of the HUD. *Check: walking up to the door shows the text, walking away hides it, and it never shows anywhere else.*
  - Passed by the owner, 2026-11-11. Built 2026-11-11. A `MessageArea` (`Messages/Back Door Blocked`, 9 by 8 m around the crate pile in front of the door) holds the text, and `GameMessage` shows it in the same panel as task 18 while the player stands in it, not while hiding. Added by **Tools > Add Game Messages**. More areas can be added anywhere by copying it and changing the text and size.
- [x] **18. Checkpoint notification (owner, 2026-11-10).** When the player steps onto a new checkpoint (`Checkpoint.At` finds one that isn't the current one), show a brief on-screen message, such as "Checkpoint reached", that fades after a couple of seconds. Not again for the checkpoint they are already on. Works in both levels. *Check: each checkpoint shows the message once when first reached, and standing on it or coming back to the current one doesn't repeat it.*
  - Passed by the owner, 2026-11-11. Built 2026-11-11. `PlayerLives` raises `CheckpointReached` the first time each checkpoint becomes the current one (the first checkpoint counts as reached from the start). `GameMessage` on `HUD/Message Panel` shows "Checkpoint reached" for 2.5 s and fades in and out (Canvas Group). The panel looks like the tutorial panel, sits under it in `Corridor_01` (y -230) and at the top in `Warehouse_01` (y -110). It was added to both levels by a one-off **Tools > Add Game Messages** (`GameMessageBuilder`, now only in git history). It is the HUD's place for short game messages, so later ones can use `GameMessage.Show`.
- [x] **19. Checkpoint near the stairs (owner, 2026-11-10).** Add a checkpoint to `Warehouse_01` in the lit pool nearest the staircase, so a catch on the way up doesn't send the player back to the front. Probably the skylight pool `S1` at about X 18.3, Z 35.8, about 9 m from the stair foot (confirm with the owner). *Check: reaching it, then getting caught, returns the player there, and task 18's message shows when it is reached.*
  - Passed by the owner, 2026-11-11. `Checkpoint_02` under `Checkpoints`, in skylight S1's pool (`LightPool_S1`), placed by a one-off **Tools > Add Stairs Checkpoint** (`StairsCheckpointBuilder`, now only in git history) and nudged by hand.
- [x] **23. Make the back door itself green (owner, 2026-11-11).** The blocked back door of `Warehouse_01` (`Back_Door`) is the same rusted steel as the warehouse walls (`Warehouse_Rusted_Steel`), so only its green outline (`Door_Outline_*` on `Highlight_Exit`) marks it. Make the door panel green too, so the exit reads at a glance. The owner remembers it being green once, but every committed version has the door on rusted steel, so that was probably never saved. Like the outline, it must not read as a lit pool: a painted or faintly glowing green, with no light, below bloom. Goes with task 13. *Check: from the front door, the back door reads as the green exit, and it doesn't look like a safe pool.*
  - Passed by the owner, 2026-11-11. `Back_Door` uses a new `Warehouse_Back_Door` material, painted green (Lit, so lamps and the flashlight still light it) with a faint green emission (about 0.16 at most, far below the bloom threshold of 1), so it shows in the dark without throwing light. The outline stays the brighter green. `WarehouseBuilder` still puts the door on rusted steel, but rerunning it would also wipe the highlights.

### Levels
- [ ] **25. Connect the corridor to the warehouse (owner, 2026-11-11).** Today each level stands alone: reaching `Corridor_01`'s `End Point` stops the game and Interact restarts the corridor, and `Warehouse_01` isn't in the build list at all (only `Corridor_01` is). Make reaching the corridor's end load `Warehouse_01`, so the game plays as tutorial then warehouse. Add `Warehouse_01` to the build list after `Corridor_01`. The warehouse's own end point keeps the "finished" screen for now, since there's no next level. To decide with the owner: whether the corridor's end leads straight in, with a fade, or shows a short "level complete" first, and whether lives carry over or refill (they refill today, because each scene starts fresh). *Check: in a PC build, finishing the corridor puts the player at the warehouse start, and getting caught in the warehouse returns them to a warehouse checkpoint, never to the corridor.*

- [ ] **28. New jump sound (owner, 2026-11-11, important, do soon).** The jump's sound needs replacing. Today the only jump sound is the `landing` clip on `PlayerNoise`, played when he lands (quieter when crouched); there is no take-off sound. Replace the landing sound, and ask the owner whether the jump should also get a take-off sound. Sound only: the noise enemies hear stays on `NoiseRules`, and the player stays silent to them inside a lamp pool. *Check: jumping and landing sound right in both levels, standing and crouched.*

### Look and feel
- [ ] **26. Wooden crate model (owner, 2026-11-11).** The crates are still grey boxes. Give them a real design: an old wooden crate of planks with a frame, but solid, with no gaps between the planks, so nothing can be seen through it and it stays simple to build. One model for every crate, hiding crates (`Hide_Crate`) included, and the colliders and `VisionBlocker` layer stay as they are, so stealth doesn't change. *Check: the crates read as wooden crates under a lamp and by flashlight, nothing shows through them, and hiding and monster sight work as before.*
- [ ] **27. Dimmer always-on flashlight (owner, 2026-11-11).** The normal flashlight is too bright since task 15 raised it (intensity 4 to 12, range 9 to 13). Turn it down so the dark feels dark again and holding F for the bright beam is worth it, while the way ahead stays readable. *Check: with the normal flashlight the dark is clearly dark, the bright beam is a clear step up, and lamp pools still read as the safe light.*

### Combat
- [ ] **20. Punch sounds (owner, 2026-11-10).** A swing sound on every punch, and a separate hit sound when it lands and stuns an enemy. A punch the enemy shrugs off (stunned too recently) gets a duller sound, so the player can tell it didn't work. `PlayerCombat` already raises `Punched`, and `EnemyBrain.TryStun` already reports whether a punch landed. *Check: a miss, a stun and a shrugged-off punch each sound different.*
- [x] **21. A punched enemy searches instead of chasing (owner, 2026-11-10).** Today, when the stun ends, the enemy goes straight back to Chase if it still has the player spotted, which after a close punch is nearly always (`EnemyBrain.TickStunned`). Change it so the stun always ends in Search, around where it was punched, so a punch gives the player a real chance to slip away. From Search it can still spot the player again in the normal way. Add an edit mode test for it. *Check: after a punch, the enemy recovers into Search (its search colour), and it only chases again if it sees the player afresh.*
  - Passed by the owner, 2026-11-11. Built 2026-11-11. `TickStunned` now always resets the detection meter and enters Search, and a stunned enemy stops updating where it last saw the player, so it searches around where the punch landed. Tests: `APunchedEnemySearchesWhereItWasPunchedEvenIfItStillSeesThePlayer` and `APunchedEnemyMustNoticeThePlayerAfreshBeforeChasingAgain` (replacing `AChaseResumesAfterAStun`).
- [ ] **22. Wind sound on the dodge roll (owner, 2026-11-10).** A short whoosh of air when the player rolls, timed to the roll and played from the player. It is only a sound for the player, not a noise event that enemies hear, unless the owner decides otherwise. *Check: every dodge, including a roll straight back, plays the whoosh once, in time with the roll.*

### Tutorial
- [x] **24. Tutorial in `Corridor_01` (owner, 2026-11-10, plan agreed 2026-11-11).** Turn the corridor into the first level that teaches the game, so a new player knows how to play before `Warehouse_01`. Short prompts in a panel at the top of the HUD, one at a time, each shown once per launch of the game (not again after a restart or a catch; saving isn't built yet). Keys come from `InputSystem_Actions` (keyboard and mouse only for now), so the text stays right if a binding changes. The warehouse is left alone. This replaces the task 23 tutorial draft on the unmerged `warehouse-design` branch, whose number is taken on `main`. *Check: a first-time player gets through the corridor using only the prompts, and each prompt shows once, at the right place.* Passed by the owner, 2026-11-11.
  - Built by **Tools > Build Corridor Tutorial** (`TutorialBuilder`, now removed and kept in git history). It adds a `Tutorial` object whose children are the prompts, in the order they show, and `HUD/Tutorial Panel`. Each prompt's text, when it shows and when it hides can be edited on its `TutorialPrompt`; write `{ActionName}`, such as `{Sprint}`, for a key.
  - Controls, in order, in the start yard (no monsters): move and look, sprint, jump, crouch, the visibility bar, the flashlight and holding F, then dodge and punch. Each stays until the player has done it, or until they walk on to the next spot.
  - Rules, in the first stretch of the corridor: the first lit pool (light is safe, then the monster's glow colours), the first time a monster notices the player (urgent, interrupts), the first hiding spot in reach, Checkpoint_02 (lives), the first catch (urgent), and "The rest is up to you" at z 33. The Monster_02 stretch has no prompts.
  - The queue and the text rules are plain C# (`TutorialQueue`, `TutorialRules`) with edit mode tests in `TutorialTests`.
  - Open from the plan: Monster_01 starts its patrol at z 10, 5 m past the first pool, so a new player might be spotted before the light lesson. Left as it is until the playtest shows whether it matters.

### Realistic human
The player's design model is in `Art/Human`, outside `Assets` so Unity doesn't import it. `design_sheet.png` shows it from every side, then under the game's real lamp, colour grade and follow camera. To rebuild it, run these in order (Blender 5.2 with the MPFB extension, about a minute in total):

```
blender -b --python Art/Human/body.py     # body, face and skeleton from MPFB
blender -b --python Art/Human/human.py    # clothes, hair, eyes and skin, saved to human.blend
blender -b --python Art/Human/sheet.py    # design_sheet.png
blender -b --python Art/Human/game.py     # game version: Human.fbx and textures in Assets/Art/Human (about 5 minutes)
blender -b --python Art/Human/sheet.py -- --game    # game_sheet.png, the game version under the same views
```

After `game.py`, the imports and materials were set up by **Tools > Build Human Stage** (`HumanStageBuilder`, now only in git history), which rebuilt `Human_Stage`. The `Human_Stage` scene was removed on 2026-11-11 and is kept in git history. Each part's share of the triangles is in `PARTS` in `game.py`.

Shape is tuned by named values in the scripts: `MACRO` and `DETAILS` in `body.py` for the body and face, `SLEEVE_RADII` in `clothes.py` for the sleeves, `NAPE_ABOVE_COLLAR` in `head.py` for the hair length at the back. The model is saved in an A-pose; `sheet.py` lowers the arms only for the renders.

- [x] **H1. Approve the design.** Approved by the owner, 2026-11-10.
- [x] **H2. Game version.** Approved by the owner, 2026-11-10: 28,625 triangles after H3 removed the hidden thighs, one mesh with four materials (coat atlas 4096, clothes and head 2048, hair and eyes 1024), and `Human_Stage` with `HumanStageBuilder` (removed after H4, kept in history).
- [x] **H3. Rig for Unity.** Approved by the owner, 2026-11-10. He imports as Humanoid with the T-pose set by `HumanStageBuilder`. The lower coat has 8 columns of 2 bones, hinged at the belt, and `CoatSkirt` swings each column out when a knee or shin reaches it. `game.py` leaves out the trousers above `TROUSERS_HIDDEN_ABOVE`, because they are always under the coat and a raised thigh would come through it. Known limit: a deep crouch (thighs about 70 degrees up) still shows a small slit at the knee.
- [x] **H4. Put him in the game.** Passed by the owner, 2026-11-10. **Tools > Put Human In Game** (`HumanGameBuilder`) imports the clips in `Assets/Art/Human/Animations`, builds `Human.controller` and the `Human` prefab, and puts him under `Player/Body` in both levels in place of the capsule. *Check: both levels can be crossed with no sliding feet and nothing clipping through walls.*
  - Clip speeds are matched to the player's: the walk clip plays at 1.3x for 3 m/s, the sprint at 1.33x for 5.5 m/s, the crouch walk at 1.18x for 1.5 m/s. The roll plays at 1.8x to fit the dodge, and the punch at 1.9x to fit its cooldown.
  - Dodge and punch (owner, 2026-11-10): the dodge is 0.45 s at 7 m/s (3.2 m) instead of 0.22 s at 11 m/s (2.4 m), so the roll can be seen. Punch reach is 2.5 m instead of 2 m. `HumanGameBuilder` writes these into both scenes. A longer dodge also means a longer window in which an enemy's catch misses.
  - The crouch walk clip stands much taller than the crouched idle, so `PlayerAnimator` lowers his hips by `crouchWalkDrop` with the feet held in place.
  - Footstep sounds come from events in the walk and sprint clips. The noise enemies hear still runs on `PlayerNoise`'s stride timer.
  - `CoatSkirt` hangs toward gravity, so the coat stays down when he leans, and hands back to the body when he tips over in the roll.
  - `Ducking.fbx` was downloaded but not used. It is removed, and kept in history at commit `3f377fb`.
  - `HumanStageBuilder` and `HumanGameBuilder` are removed now that he is in the game, and kept in git history. Don't bring back **Build Human Stage** as it was: it copies `Player/Body` for its capsule, which is now the Human.
  - The top of the hat is at 1.89 m against the 1.8 m controller, so it can clip low ceilings.
- [x] **H5. Look ahead while crouch-walking (owner, 2026-11-11).** When he walks crouched, his head points down, so he isn't looking where he's going. Tilt the head up, so he faces the way he walks. `PlayerAnimator` already lowers his hips during the crouch walk (`crouchWalkDrop`), so the tilt can go there too, as a head look-at or a bone tilt that fades in with the crouch walk. *Check: crouch-walking, his face points ahead rather than at the floor, and standing up blends back with no snap.*
  - Done 2026-11-11: `PlayerAnimator` uses the humanoid look-at in `OnAnimatorIK`, aimed at a point 4 m ahead at head height. The head does most of the turn (`lookAheadHead` 1) and the spine helps a little (`lookAheadBody` 0.15), so he still reads as crouched. It fades in and out over `lookAheadFade` (0.25 s) with the same crouch-walk amount as the hip drop. The hip drop itself still switches off in one frame when he stands mid-walk.

### Realistic monster (the Wick)
The design model is approved and kept in `Art/Wick`, outside `Assets` so Unity doesn't import it yet: `wick.blend`, `wick.py` (the Blender script that builds it) and `wick_design_sheet.png`. `Assets/Scenes/MonsterStage_01` was the room for trying it in, with a `Monster Spot` to put it under. The scene and its `AlertPreview` script were removed on 2026-11-11 once the Wick was in the game, and are kept in git history, like `MonsterStageBuilder`.

To rebuild the game version (Blender 5.2), run these in order:

```
blender -b --python Art/Wick/game.py               # Wick.fbx and textures in Assets/Art/Wick, and Art/Wick/game.blend
blender -b --python Art/Wick/sheet.py              # game_sheet.png, the design sheet's views of the game version
blender -b --python Art/Wick/sheet.py -- --poses   # pose_sheet.png, the rig bent into a grab and a stride to check the weights
```

Each part's triangle budget is in `PARTS` in `game.py`. The Unity import (Humanoid, the `Wick_Skin` material, the seam on `Monster_Alert`) was set up by a one-off **Tools > Put Wick On Stage**, which also put the Wick under `Monster Spot` in `MonsterStage_01` with `AlertPreview` stepping the seam through every state every 2 s. It was removed before it was committed, so it is not in git history. Its settings are kept in the `.meta` files, and `WickGameBuilder` now sets the T-pose.

- [x] **M1. Make the Wick game-ready.** Passed by the owner, 2026-11-11. It is a design model now: about 190,000 triangles, with no UVs, baked textures or rig. Build a lower-poly mesh (about 25,000 triangles, like the human's 28,625, since two can be on screen), UV it, bake the skin, char and surface detail from the sculpt into textures for URP, and give it a humanoid rig so ready-made clips can drive it. Clips needed: idle, walk (patrol), run (chase), grab (the 0.6 s wind-up and catch), stunned (2.5 s), and ideally a look-around for Search. Scripted in Blender like the human (Blender 5.2 is installed). Keep the seam and spine glands as one separate mesh using `Monster_Alert`, so `Enemy` can colour it through its `Alert Light` reference. Optional: a better face, the weakest part up close. *Check: under `Monster Spot` in `MonsterStage_01`, it reads as the design sheet does in lamp light and by flashlight, and the seam changes colour with each state.*
  - Built in Blender (2026-11-11): 24,900 triangles. The body has 21,500 and the claws, teeth and eyes 2,500, all on one 4096 skin atlas (`Wick_Skin`). The seam and glands are a separate 900-triangle mesh, `Wick_Alert`, on `Monster_Alert`. `game_sheet.png` matches the design sheet's views. `pose_sheet.png` bends the rig into a grab and a stride to show the weights hold.
  - The spine glands were buried about 1 cm under the skin in the design (`wick.py` places them before it raises the spine knuckles), so they never showed, even on the design sheet. `game.py` lifts each one onto the skin and makes it 1.5 times larger (`GLAND_SCALE`, `GLAND_RAISE`). `game_sheet.png` has an extra lamp-lit view from behind to show them. `wick.py` and the design sheet are unchanged.
  - The rig uses Mixamo bone names, with fingers but no jaw: the teeth and eyes follow the head. Bone heat weights only solve at 10 times the size, so `game.py` scales up for that step.
  - The body is unwrapped by seams, one island per body part, with seams on the back or underneath. Smart project broke the bumpy sculpt into thousands of single-triangle islands.
- [x] **M2. Put the Wick in the game.** Passed by the owner, 2026-11-11: chase, grab and stun play right, it fits along the catwalk rails, no console errors, and the edit mode tests pass. Replace the capsule on the monsters in both levels (`Corridor_01` and `Warehouse_01`). Its arms span about 1.05 m against the agent's 0.9 m, and its head reaches 0.65 m forward of the agent's centre, so widen the agent (owner, 2026-11-11) and rebake the NavMesh in both levels, `Lit` areas included. Build a Wick prefab and a small animator script that plays walk or run from the agent's speed and grab or stunned from `EnemyState`. Its seam becomes the `Alert Light`, and the sphere above the head goes. *Check: it patrols and chases both levels with no visible clipping through walls, and each state plays the right clip.*
  - Built 2026-11-11 by **Tools > Put Wick In Game** (`WickGameBuilder`, now only in git history).
  - Clips in `Assets/Art/Wick/Animations`, from the owner's Mixamo downloads: `Idle` (Monster Idle), `Walk` (Walking), `Run` (mutant run), `Grab` (mutant swiping, the wind-up to the swipe), `Stunned` and `LookAround` (Looking Around). The owner keeps the original downloads in `~/Downloads/creature`.
  - Mixamo files import at twice the size (hips 2 m up), so clip speeds were measured at the Wick's leg length (hips 1.06 m up). On the Wick the walk covers 1.23 m/s and the run 2.31 m/s. The locomotion blend plays the walk at 1.46x for patrol, the run at 1.21x for investigate and 2.08x for chase, so the feet don't slide. If the chase looks too frantic, swap the run clip or lower the chase speed.
  - The grab clip plays the wind-up and swipe in the 0.6 s grab, and the stunned clip is sped up to fit the 2.5 s stun. The look-around plays on the head only, during Search, while the legs walk.
  - The walk looked stiff (owner, 2026-11-11): the walk clip is upright, so the Wick lost its hunch and walked with a rigid torso. `WickAnimator` now bends the spine 21 degrees forward (`hunch`) at walking pace. The bend fades out toward standing still and toward running, because `Monster Idle` and the mutant run are hunched already, and it goes during the grab. The arms still swing very little, because the clip barely swings them. The hunch is good (owner, 2026-11-11).
  - The legs looked stiff (owner, 2026-11-11), because the walk clip keeps the knees nearly straight. `WickAnimator` now lowers the hips by `walkDrop` (0.12 m) at walking pace with the feet held in place, so the knees bend, the same way `PlayerAnimator` handles the human's crouch walk. Each knee gets an IK hint to point forward, because without one the IK flipped the knees from side to side and the legs snapped. This needs the IK pass on the base layer of `Wick.controller`.
  - The Avatar's T-pose now has the spine straight up, because the mutant clips are already hunched and would otherwise add their lean to the modelled one. The neck and head keep their modelled angle to the chest.
  - The agent radius is 0.55 m in the NavMesh settings and on every monster, and `LampZone` widens each `Lit` area by 0.55 m instead of 0.45 m, so the arms stay out of the light. The tool rebakes both levels and warns in the Console if any patrol route no longer joins up outside the light. The warehouse catwalks are 1.5 m wide, which leaves the agent a strip about 0.4 m wide.

### Decisions
- **The human (owner, 2026-11-09).** A noir man in a top hat with a dark red band, sitting just above the brows; a belted double-breasted trench coat to the knee; white shirt, striped tie, leather gloves, matte dress shoes; dark hair down to the collar at the back. The body and face come from MPFB and the animation from Mixamo; everything else is scripted. Don't use the `game-human` or `game-human-advanced` worktrees as references for this version.
- **The Wick (owner, 2026-11-09).** The realistic monster is a starved, hunched creature burned by light: wet ashen skin, with hands, forearms and feet charred black. It is 1.95 m tall with its eyes at 1.7 m, matching the height `Enemy` sees from. Its long arms hang almost to the shins to suit the 1.4 m catch reach. A glowing seam down the breastbone and glands down the spine replace the sphere above the head as the warning cue, so it reads from the front and the back.
- **Light rule (owner, 2026-11-08).** Enemies are creatures of the dark and fear light. They won't enter a lit pool, so lamps are true safety and the dark is where the danger is. Inside a pool the player makes no noise enemies can hear, from footsteps (even sprinting) or landing a jump (owner, 2026-11-11), so a pool is completely safe. Objects knocked over in a pool still make noise. Visibility measures how exposed the player is in the dark.
- **Light doesn't stop a chase (owner, 2026-11-09).** Standing in a pool to shake off a chasing enemy felt like cheating. An enemy already in Chase now follows the player into lit pools and doesn't give up at the edge. Every other state still stays out of the light. This replaces the wait at the edge described in task 9.
- **Lit pools are a NavMesh area (2026-11-09).** With two monsters, the carving NavMesh Obstacle from task 9 would open a pool for every monster at once. Each pool is now a `Lit` area in the baked NavMesh, and each monster allows or forbids that area for itself. A lamp that goes out will need a NavMesh rebake to open its pool.
- **Getting caught (2026-11-09, starting values, open to change).** A chasing enemy in reach winds up for 0.6 s, with a white light and a sound, so the player can react. A catch costs one of 3 lives and returns the player to the last checkpoint touched; losing the last life refills them and returns to the first checkpoint. A punch stuns for 2.5 s and can't land again for 6 s, so an enemy can't be stun-locked. A dodge moves the player out of reach. Every catch also resets every monster (owner, 2026-11-11): each one goes back to where it started the level, at the start of its route, in Patrol with no memory of the player, so a second monster that heard the chase can't be waiting at the checkpoint.
- **Third-person camera behind the player (owner, 2026-11-08).** The player can see their own shadow and nearby enemies, and it suits Arkham-style combat later.
- **Corridor layout (owner, 2026-11-08).** 6 m wide with lamps on alternating walls, so an enemy has a dark lane about 2.4 m wide past each pool. With lamps in the middle, a pool would fill the corridor and trap the enemy in one gap.
- **Flashlight (owner, 2026-11-09).** The player carries a small dim flashlight that points where they look. It is for seeing by and doesn't change exposure.
- **Bright flashlight (owner, 2026-11-10).** Holding F makes the flashlight brighter and further reaching, but adds 0.5 exposure, even inside a lamp pool, so enemies notice the player faster. It goes out while hiding.
- **Street car (owner, 2026-11-10).** The real `Car.prefab` (from `game-car`) replaces the graybox car on the street outside `Warehouse_01`. It is a static prop: one box collider, all parts on the `VisionBlocker` layer, no driving. It sits outside the baked NavMesh volume, so no rebake is needed.
- **PC first (owner, 2026-11-08).** Mobile is secondary and may never ship. If it comes back, it needs touch controls and a new lighting pass, because the Mobile profile has lamp shadows off. Baking the lamps is the likely fix, but baked lamps can't go out and characters don't throw shadows from them.

### Not in this list
Menus, saving, final art, bosses, the calmer stealth path, dodge-and-counter combat, the final controller layout (current buttons are a placeholder), and anything for mobile. Pick these up after task 12 shows the corridor is fun.

## Game idea: Noir horror stealth (working concept)

**Pitch:** A Spider-Man-noir-style horror game, but not a Spider-Man game. No swinging. You walk through the dark trying to reach the other side while enemies hunt you.

**Core focus:** Sneaking first, with combat as the backup.
- Horror comes from feeling vulnerable and hunted.
- Getting caught is a consequence, not a scripted requirement.
- Every encounter should be solvable by stealth if the player is good enough, ideally even boss moments (still undecided).
- A clean stealth run could unlock a calmer, different path.

**Design goals (from the owner's frustration with other games):**
- No forced detection. Stealth must be a real option.
- Clear visual cue for how visible the player is.
- A warning beat before enemies fully spot you.
- Hiding spots to recover when things go wrong.

**Mechanics:**
- Pools of light are safe zones, dark stretches are risky.
- Enemies fear light and won't enter it unless they are already chasing the player. They hunt the dark with simple vision and hearing.
- Combat starts tiny: one punch, one dodge, one enemy type. It can grow into an Arkham-style dodge and counter later.

**Why it's feasible:**
- Unity's lighting suits noir.
- A few moody corridors run lighter than a big open city, which leaves room for real-time shadows on PC.

**Open questions:**
- Fully stealthable through boss encounters?
- What exactly gets the player caught in other games (spotting too fast, unclear light, no escape)?

## Rules

### Building things
- **Build things in the scene hierarchy, not in code.** Menus, buildings, vehicles, anything visible are real objects in the scene or a prefab, so the owner can edit them. Scripts hold references and run the behaviour.
- The usual way: a one-off Tools menu script that builds the objects, then is deleted (kept in git history).
- Plain positioned UI, no layout groups unless asked.
- **Every button is a copy of one shared UI button prefab.** Change only the name, place, size and text on a copy; restyle in the prefab. The prefab doesn't exist yet; make it when the first button is needed.

### Working in this project
- Each bug fix gets its own commit, so the history shows it as a fix.

### Releasing
- Change only Project Settings > Player > Version. The Android version code is set from it on every Android build. This needs `Assets/Editor/AndroidVersionCode.cs` from Farming-Simulator-94, which is not in this project yet. Only add it if Android ships.
