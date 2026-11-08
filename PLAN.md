# Last Lamp: Plan

**When asked "what's next?", the answer is the first open item under NEXT UP** (order set by the owner).
**To continue in a new chat:** "Read PLAN.md and start the NEXT UP task."

Unity 6000.0.85f1, Universal Render Pipeline (URP).

## NEXT UP (in order)

Nothing yet. The owner sets the first task.

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
- A few moody corridors run lighter on phone than a big open city.

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
- Change only Project Settings > Player > Version. The Android version code is set from it on every Android build. This needs `Assets/Editor/AndroidVersionCode.cs` from Farming-Simulator-94, which is not in this project yet.
