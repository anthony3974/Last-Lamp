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
- [ ] **13. Make the way through readable (owner, 2026-11-09).** In `Warehouse_01`, highlight the exit door (the blocked back door, owner 2026-11-09), the path routes and the stairs, or turn up the overall brightness if that reads better. Highlights must not look like lit pools, because only lit pools are safe. *Check: a first-time player can find the stairs and the exit without help, and can still tell safe light from dark.*
- [ ] **14. Opening front door (owner, 2026-11-09).** The front door of `Warehouse_01` is only a gap in the wall for now. Give it a real door that opens. Monsters still must not follow the player outside, and locking the door behind the player stays the fallback if they do. *Check: the door opens and the player can go in and out, and a chasing monster stops at the doorway.*

- [ ] **15. Make the game brighter (owner, 2026-11-10).** The game is too dark overall. Raise the base brightness (ambient light, volume exposure, the dim flashlight) without losing the noir look, and without lit pools stopping reading as the only safe light. Task 13 covers highlighting the way through in `Warehouse_01`; this one is the game-wide level, and the two can share the same pass. *Check: the corridor and the warehouse are easy to read on a normal monitor, the pools are still clearly the safe spots, and the stealth rules are unchanged.*

- [ ] **16. Intro cutscene for `Warehouse_01` (owner, 2026-11-10, maybe).** A first-time player doesn't know how to get through. Before play starts, a short camera fly-through shows the route: in at the front door, up the stairs, along the catwalk, to the exit. It renders at full brightness, so the layout is clear, then fades back to the normal dark look when control returns. Skippable, and only shown the first time. *Check: a first-time player who watched it heads for the stairs without help.*
- [ ] **17. "This door is blocked" text at the back door (owner, 2026-11-10).** When the player gets close to the blocked back door of `Warehouse_01`, show a short on-screen text box: "This door is blocked." It hides again when they walk away. Plain positioned UI, like the rest of the HUD. *Check: walking up to the door shows the text, walking away hides it, and it never shows anywhere else.*
- [ ] **18. Checkpoint notification (owner, 2026-11-10).** When the player steps onto a new checkpoint (`Checkpoint.At` finds one that isn't the current one), show a brief on-screen message, such as "Checkpoint reached", that fades after a couple of seconds. Not again for the checkpoint they are already on. Works in both levels. *Check: each checkpoint shows the message once when first reached, and standing on it or coming back to the current one doesn't repeat it.*
- [ ] **19. Checkpoint near the stairs (owner, 2026-11-10).** Add a checkpoint to `Warehouse_01` in the lit pool nearest the staircase, so a catch on the way up doesn't send the player back to the front. Probably the skylight pool `S1` at about X 18.3, Z 35.8, about 9 m from the stair foot (confirm with the owner). *Check: reaching it, then getting caught, returns the player there, and task 18's message shows when it is reached.*

### Combat
- [ ] **20. Punch sounds (owner, 2026-11-10).** A swing sound on every punch, and a separate hit sound when it lands and stuns an enemy. A punch the enemy shrugs off (stunned too recently) gets a duller sound, so the player can tell it didn't work. `PlayerCombat` already raises `Punched`, and `EnemyBrain.TryStun` already reports whether a punch landed. *Check: a miss, a stun and a shrugged-off punch each sound different.*
- [ ] **21. A punched enemy searches instead of chasing (owner, 2026-11-10).** Today, when the stun ends, the enemy goes straight back to Chase if it still has the player spotted, which after a close punch is nearly always (`EnemyBrain.TickStunned`). Change it so the stun always ends in Search, around where it was punched, so a punch gives the player a real chance to slip away. From Search it can still spot the player again in the normal way. Add an edit mode test for it. *Check: after a punch, the enemy recovers into Search (its search colour), and it only chases again if it sees the player afresh.*
- [ ] **22. Wind sound on the dodge roll (owner, 2026-11-10).** A short whoosh of air when the player rolls, timed to the roll and played from the player. It is only a sound for the player, not a noise event that enemies hear, unless the owner decides otherwise. *Check: every dodge, including a roll straight back, plays the whoosh once, in time with the roll.*

### Realistic human
The player's design model is in `Art/Human`, outside `Assets` so Unity doesn't import it. `design_sheet.png` shows it from every side, then under the game's real lamp, colour grade and follow camera. To rebuild it, run these in order (Blender 5.2 with the MPFB extension, about a minute in total):

```
blender -b --python Art/Human/body.py     # body, face and skeleton from MPFB
blender -b --python Art/Human/human.py    # clothes, hair, eyes and skin, saved to human.blend
blender -b --python Art/Human/sheet.py    # design_sheet.png
blender -b --python Art/Human/game.py     # game version: Human.fbx and textures in Assets/Art/Human (about 5 minutes)
blender -b --python Art/Human/sheet.py -- --game    # game_sheet.png, the game version under the same views
```

After `game.py`, the imports and materials were set up by **Tools > Build Human Stage** (`HumanStageBuilder`, now only in git history), which rebuilt `Human_Stage`. Each part's share of the triangles is in `PARTS` in `game.py`.

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

### Decisions
- **The human (owner, 2026-11-09).** A noir man in a top hat with a dark red band, sitting just above the brows; a belted double-breasted trench coat to the knee; white shirt, striped tie, leather gloves, matte dress shoes; dark hair down to the collar at the back. The body and face come from MPFB and the animation from Mixamo; everything else is scripted. Don't use the `game-human` or `game-human-advanced` worktrees as references for this version.
- **Light rule (owner, 2026-11-08).** Enemies are creatures of the dark and fear light. They won't enter a lit pool, so lamps are true safety and the dark is where the danger is. Visibility measures how exposed the player is in the dark.
- **Light doesn't stop a chase (owner, 2026-11-09).** Standing in a pool to shake off a chasing enemy felt like cheating. An enemy already in Chase now follows the player into lit pools and doesn't give up at the edge. Every other state still stays out of the light. This replaces the wait at the edge described in task 9.
- **Lit pools are a NavMesh area (2026-11-09).** With two monsters, the carving NavMesh Obstacle from task 9 would open a pool for every monster at once. Each pool is now a `Lit` area in the baked NavMesh, and each monster allows or forbids that area for itself. A lamp that goes out will need a NavMesh rebake to open its pool.
- **Getting caught (2026-11-09, starting values, open to change).** A chasing enemy in reach winds up for 0.6 s, with a white light and a sound, so the player can react. A catch costs one of 3 lives and returns the player to the last checkpoint touched; losing the last life refills them and returns to the first checkpoint. A punch stuns for 2.5 s and can't land again for 6 s, so an enemy can't be stun-locked. A dodge moves the player out of reach.
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
