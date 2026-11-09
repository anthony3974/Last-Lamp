# Last Lamp: Plan

**When asked "what's next?", the answer is the first open item under NEXT UP** (order set by the owner).
**To continue in a new chat:** "Read PLAN.md and start the NEXT UP task."

Unity 6000.0.85f1, Universal Render Pipeline (URP).

## NEXT UP (in order)

Goal of this list: one graybox corridor, playable on PC, that you can cross by stealth alone. Each task is small enough for one session and ends with a check. Tick it off when the check passes.

Unity locks the project while the editor is open, so change Project Settings through a one-off Tools menu script (see Rules) rather than by editing the files on disk.

### Project setup
- [ ] **1. Identity and platform.** Company `Tony3974`, product `Last Lamp`, package `com.Tony3974.LastLamp` (matches Farming-Simulator-94). Build for Linux on this machine; Windows build support is already installed for when the game is shared. *Check: an empty PC build opens and runs.*
- [ ] **2. Clear out the template.** Delete `Assets/TutorialInfo` and `Assets/Readme.asset`. Remove the Visual Scripting, Multiplayer Center and Version Control packages. *Check: no console errors after Unity reloads.*
- [ ] **3. Project structure.** Folders under `Assets`: `Scenes`, `Scripts/{Player,Stealth,Enemies,UI}`, `Scripts/Tests`, `Prefabs`, `Materials`, `Editor`. One `LastLamp` assembly for the game and one `LastLamp.Tests` for edit mode tests. Layers: `Player`, `Enemy`, `VisionBlocker`, `Interactable`. Rename `SampleScene` to `Corridor_01`. *Check: one empty edit mode test passes.*
- [ ] **4. Input and camera packages.** Add Cinemachine 3. In `InputSystem_Actions`, add `Dodge` and remove `Jump`, `Previous` and `Next`. *Check: all remaining actions have keyboard and gamepad bindings, and the project compiles.*

### Prove the look (the game depends on it, so it comes first)
- [ ] **5. Noir lighting test.** Build a graybox corridor with six lamps. Use real-time Spot lights with shadows, which the PC profile already supports, so the player and enemies throw shadows and lamps can flicker or go out. Use a volume for black and white, high contrast and vignette. *Check: on PC the shadows are hard and clear, a moving capsule throws a shadow, the pools read clearly, and it holds 60 fps.*

### Core loop in graybox
- [ ] **6. Player.** Third-person walk, crouch and sprint with a Cinemachine follow camera. Keyboard and mouse, plus gamepad. *Check: the corridor can be walked end to end with both.*
- [ ] **7. Light zones and the visibility cue.** Each lamp has a trigger volume that marks its pool. Gameplay reads these volumes and never the rendered light, so tuning the look can't change the rules. Combine zone, crouch and movement into one 0 to 1 visibility value, and show it on screen. Needs Decision A. *Check: the cue changes clearly when entering a pool, crouching and running.*
- [ ] **8. Noise.** Footsteps (loudest when sprinting, silent when crouched) and knocked-over objects emit a noise event with a position and radius. Add placeholder footstep audio. *Check: a debug gizmo shows the radius for each noise.*
- [ ] **9. One enemy.** NavMesh patrol, vision cone blocked by `VisionBlocker`, and hearing from task 8. States: Patrol, Suspicious (the warning beat), Investigate, Chase, Search, back to Patrol. Keep the detection maths in plain C# so it can be tested. *Check: edit mode tests prove detection can never go from unseen to fully spotted without passing through Suspicious, and the enemy gives a visible and audible warning in play.*
- [ ] **10. Hiding spot.** Interact to enter and leave. Entering unseen ends a chase and the enemy goes to Search. *Check: a chase can be escaped by hiding, and hiding in full view of the enemy does not work.*
- [ ] **11. Getting caught.** Being caught costs something and returns the player to a checkpoint, with no scripted capture anywhere. Add one punch and one dodge against the one enemy type. *Check: caught, fought and escaped outcomes all play out without getting stuck.*
- [ ] **12. Finish the corridor and playtest.** Add an end point and restart. Lay out lamps, one enemy route and two hiding spots. *Check: in a PC build, the corridor can be crossed without being spotted, and each Design Goal below has been tried and noted as working or not.*

### Decisions needed
- **Decision A (before task 7): what does light do?** The idea below says pools of light are safe and the dark is risky, which is the reverse of most stealth games, where darkness hides you. It also says enemies react to light, and asks for a cue for how visible the player is, and those two need defining under this rule. Recommended, because it keeps the owner's rule and fits the title: the enemies are creatures of the dark and won't enter a lit pool, so lamps are true safety, and visibility measures how exposed the player is in the dark from movement, noise and distance.
- **Assumed: third-person camera.** Taken from the Arkham-style combat goal. Changing it affects tasks 4 and 6.
- **PC first (owner, 2026-11-08).** Mobile is secondary and may never ship. If it comes back, it needs touch controls and a new lighting pass, because the Mobile profile has lamp shadows off. Baking the lamps is the likely fix, but baked lamps can't go out and characters don't throw shadows from them.

### Not in this list
Menus, saving, final art, bosses, the calmer stealth path, dodge-and-counter combat, and anything for mobile. Pick these up after task 12 shows the corridor is fun.

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
- Enemies react to light and sound, with simple vision and hearing.
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
