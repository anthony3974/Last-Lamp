# Last Lamp: Plan

**When asked "what's next?", the answer is the first open item under NEXT UP** (order set by the owner).
**To continue in a new chat:** "Read PLAN.md and start the NEXT UP task."

Unity 6000.0.85f1, Universal Render Pipeline (URP).

## NEXT UP (in order)

Completed and verified tasks (1–5, 7–15, 17–33, H1–H4, M1–M2) have been removed. Task 16 (an intro cutscene for `Warehouse_01`) was dropped on 2026-11-11, because the highlighted stairs already make the way through readable. Full history is in `git log -p PLAN.md`.

Unity locks the project while the editor is open, so change Project Settings through a one-off Tools menu script (see Rules) rather than by editing the files on disk.

### Next (owner approved, 2026-11-26)
- [x] **34. Title screen (awaiting owner check).** `Main` opens on New Game, Continue (greyed out until task 35) and Quit, over a dark lamp-lit backdrop. Its buttons are copies of `UIButton.prefab`, and the pause menu's Quit now returns here instead of leaving the game. *Check: a PC build opens on the title screen, and New Game starts the corridor exactly as Play does now.*
  - Done 2026-11-26: the backdrop is the new `Title` scene (a brick wall, wet asphalt, one street lamp and the man standing under it, seen by a still `Title Camera`). It has a `Level`, so `LevelLoader` loads it like a level, with the player on its start and his controls off. The menu is `Title Menu` in Main's HUD, below the fade; the HUD's play parts (lives, exposure bar, interact prompt) hide while it is up, and Esc does nothing there. The pause menu's button now reads "Quit to title". The camera brain now cuts instead of blending, so there is no sweep from the title camera after the fade. `UIButton.prefab` greys out its text when a button is off (`DimLabelWhenOff`). Continue has no On Click yet; task 35 wires it and turns it on. Built by the one-off **Tools > Build Title Screen** (`TitleScreenBuilder`). Checked in a Linux build: it opens on the title, New Game starts the corridor, and Quit to title, keyboard navigation (skipping Continue) and Quit all work.
  - Revised 2026-11-26 (owner): the lamp post stood right behind his head, so it moved to his right, with an arm that holds the lamp over him, a shade, and a faint `Glow` light that shows the arm and the top of the post. The title is now Noto Serif Condensed Light, spread out with thin spaces (legacy text has no letter spacing), with the tagline "Stay in the light." under it in the italic. The fonts and their licence (OFL) are in `Assets/Fonts`. Done by the one-off **Tools > Revise Title Screen** (`TitleScreenReviser`), which can be run again.
- [ ] **35. Saving.** Touching a checkpoint autosaves the level, the checkpoint and the lives; Continue loads them. *Check: quit at a warehouse checkpoint, relaunch, press Continue, and you're at that checkpoint with the same lives and the monsters reset.*
- [ ] **36. Write the story (owner, with Claude helping).** The foundation plays well, but there's no reason to be in the dark, so it isn't fun yet. Settle who the man is, why he's here, what the Wick are, what the last lamp means, and how it ends. *Check: a one-page story is written under Decisions, with a list of where each beat shows up in the corridor, warehouse and rowhouse.*
- [ ] **37. Put the story in the game, with a cutscene for each level (owner, 2026-11-26).** Each level (corridor, warehouse, rowhouse) gets a cutscene that carries its part of the story; what each one shows, and whether a level also needs one at its end, is decided in task 36. Cutscenes are skippable. Anything else (notes to find, his voice, the title screen) is also decided in task 36. *Check: each level's cutscene plays at the right moment and can be skipped, and a first-time player can say, after the rowhouse, who he is and why he's going on.*

### Untested / open checks
- [x] **H5. Look ahead while crouch-walking (owner, 2026-11-11, awaiting owner check).** When he walks crouched, his head points down, so he isn't looking where he's going. Tilt the head up, so he faces the way he walks. *Check: crouch-walking, his face points ahead rather than at the floor, and standing up blends back with no snap.*
  - Done 2026-11-11: `PlayerAnimator` uses the humanoid look-at in `OnAnimatorIK`, aimed at a point 4 m ahead at head height. The head does most of the turn (`lookAheadHead` 1) and the spine helps a little (`lookAheadBody` 0.15), so he still reads as crouched. It fades in and out over `lookAheadFade` (0.25 s) with the same crouch-walk amount as the hip drop. The hip drop itself still switches off in one frame when he stands mid-walk.

### Realistic human (rebuild reference)
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

### Realistic monster — the Wick (rebuild reference)
The design model is approved and kept in `Art/Wick`, outside `Assets` so Unity doesn't import it: `wick.blend`, `wick.py` (the Blender script that builds it) and `wick_design_sheet.png`. The game version built from it is imported in `Assets/Art/Wick` and is on every monster through `Wick.prefab`. `Assets/Scenes/MonsterStage_01` was the room for trying it in, with a `Monster Spot` to put it under. The scene and its `AlertPreview` script were removed on 2026-11-11 once the Wick was in the game, and are kept in git history, like `MonsterStageBuilder`.

To rebuild the game version (Blender 5.2), run these in order:

```
blender -b --python Art/Wick/game.py               # Wick.fbx and textures in Assets/Art/Wick, and Art/Wick/game.blend
blender -b --python Art/Wick/sheet.py              # game_sheet.png, the design sheet's views of the game version
blender -b --python Art/Wick/sheet.py -- --poses   # pose_sheet.png, the rig bent into a grab and a stride to check the weights
```

Each part's triangle budget is in `PARTS` in `game.py`. The Unity import (Humanoid, the `Wick_Skin` material, the seam on `Monster_Alert`) was set up by a one-off **Tools > Put Wick On Stage**, which also put the Wick under `Monster Spot` in `MonsterStage_01` with `AlertPreview` stepping the seam through every state every 2 s. It was removed before it was committed, so it is not in git history. Its settings are kept in the `.meta` files, and `WickGameBuilder` now sets the T-pose.

### Decisions
- **Each area gets its own scene (2026-11-11; built as task 29 on 2026-11-12).** `Main` holds the player and loads each level alongside it (`Corridor_01`, `Warehouse_01`, `Rowhouse_01`). Why:
  - The warehouse can't get damaged. Every building is made by a tool that rebuilds its objects; in the warehouse scene, a mistake in a new tool, or a rerun of `WarehouseBuilder`, could wipe or move hand-made work (the highlights, the green back door, the front door, the moved checkpoint). In its own scene, its tool can only touch the new building.
  - Its own NavMesh: the new building's monster paths can be rebaked without rebaking the warehouse, so checked patrol routes stay as they are. Monsters stay in their own area unless the meshes are connected at the doorway.
  - Cleaner history: each commit changes only one building, so mistakes are easy to spot and undo.
  - Two ways to join areas, decided per chapter: load the next area as a new level (as the corridor does into the warehouse), or join them into one world with Area Links so the player crosses with no break (as the rowhouse joins the warehouse).
- **The human (owner, 2026-11-09).** A noir man in a top hat with a dark red band, sitting just above the brows; a belted double-breasted trench coat to the knee; white shirt, striped tie, leather gloves, matte dress shoes; dark hair down to the collar at the back. The body and face come from MPFB and the animation from Mixamo; everything else is scripted. Don't use the `game-human` or `game-human-advanced` worktrees as references for this version.
- **The Wick (owner, 2026-11-09).** The realistic monster is a starved, hunched creature burned by light: wet ashen skin, with hands, forearms and feet charred black. It is 1.95 m tall with its eyes at 1.7 m, matching the height `Enemy` sees from. Its long arms hang almost to the shins to suit the 1.4 m catch reach. A glowing seam down the breastbone and glands down the spine replace the sphere above the head as the warning cue, so it reads from the front and the back.
- **Light rule (owner, 2026-11-08).** Enemies are creatures of the dark and fear light. They won't enter a lit pool, so lamps are true safety and the dark is where the danger is. Inside a pool the player makes no noise enemies can hear, from footsteps (even sprinting) or landing a jump (owner, 2026-11-11), so a pool is completely safe. Objects knocked over in a pool still make noise. Visibility measures how exposed the player is in the dark.
- **Light doesn't stop a chase (owner, 2026-11-09).** Standing in a pool to shake off a chasing enemy felt like cheating. An enemy already in Chase now follows the player into lit pools and doesn't give up at the edge. Every other state still stays out of the light. This replaces the first rule, where a chasing enemy waited at the edge of a pool for a short time and then gave up.
- **Lit pools are a NavMesh area (2026-11-09).** With two monsters, the carving NavMesh Obstacle each pool first had would open a pool for every monster at once. Each pool is now a `Lit` area in the baked NavMesh, and each monster allows or forbids that area for itself. A lamp that goes out will need a NavMesh rebake to open its pool.
- **Getting caught (2026-11-09, starting values, open to change).** A chasing enemy in reach winds up for 0.6 s, with a white light and a sound, so the player can react. A catch costs one of 3 lives and returns the player to the last checkpoint touched; losing the last life refills them and returns to the first checkpoint. A punch stuns for 2.5 s and can't land again for 6 s, so an enemy can't be stun-locked. A dodge moves the player out of reach. Every catch also resets every monster (owner, 2026-11-11): each one goes back to where it started the level, at the start of its route, in Patrol with no memory of the player, so a second monster that heard the chase can't be waiting at the checkpoint.
- **Third-person camera behind the player (owner, 2026-11-08).** The player can see their own shadow and nearby enemies.
- **Corridor layout (owner, 2026-11-08).** 6 m wide with lamps on alternating walls, so an enemy has a dark lane about 2.4 m wide past each pool. With lamps in the middle, a pool would fill the corridor and trap the enemy in one gap.
- **Flashlight (owner, 2026-11-09).** The player carries a small dim flashlight that points where they look. It is for seeing by and doesn't change exposure.
- **Bright flashlight (owner, 2026-11-10).** Holding F makes the flashlight brighter and further reaching, but adds 0.5 exposure, even inside a lamp pool, so enemies notice the player faster. It goes out while hiding.
- **Street car (owner, 2026-11-10).** The real `Car.prefab` (from `game-car`) replaces the graybox car on the street outside `Warehouse_01`. It is a static prop: one box collider, all parts on the `VisionBlocker` layer, no driving. It sits outside the baked NavMesh volume, so no rebake is needed.
- **No dodge-and-counter (owner, 2026-11-26).** Combat stays as it is: one punch and one dodge, as the backup when stealth fails.
- **Keyboard and mouse only (owner, 2026-11-26).** No gamepad support; it won't be tested or designed for.
- **PC first (owner, 2026-11-08).** Mobile is secondary and may never ship. If it comes back, it needs touch controls and a new lighting pass, because the Mobile profile has lamp shadows off. Baking the lamps is the likely fix, but baked lamps can't go out and characters don't throw shadows from them.

### Not in this list
- **Bosses**, after the story (tasks 36–37), which decides who or what a boss is. Whether every boss can be sneaked past is still open.
- **Calmer stealth path:** maybe; it depends on the story.
- Final art, and anything for mobile.

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
- Combat starts tiny: one punch, one dodge, one enemy type. It stays that small (owner, 2026-11-26).

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
- **Every button is a copy of one shared UI button prefab** (`Assets/Prefabs/UIButton.prefab`, made in task 33). Change only the name, place, size and text on a copy, and set what it does in its On Click; restyle in the prefab. An edit mode test fails if a button in `Main` isn't a copy of it.

### Working in this project
- Each bug fix gets its own commit, so the history shows it as a fix.

### Releasing
- Change only Project Settings > Player > Version. The Android version code is set from it on every Android build. This needs `Assets/Editor/AndroidVersionCode.cs` from Farming-Simulator-94, which is not in this project yet. Only add it if Android ships.
