# Last Lamp

A noir horror stealth game made with Unity. You cross the dark while monsters hunt you. They fear light, so every lamp is a safe place until one of them is already chasing you, and every stretch between lamps is a risk.

Sneaking comes first, and combat is only the backup. Stealth should always be a real option: enemies give a warning before they spot you, and you can always see how exposed you are.

PC is the main platform. Mobile may come later.

## Current state

Early prototype with three levels. The levels are still mostly plain shapes, but the player and the monster are finished models.

The `Main` scene holds everything the levels share: the player, the camera, the HUD and the night's light and sky. Each level is a scene of its own that loads alongside `Main` in the background, so a level can be edited without touching the others. Areas can also be joined into one world, where the next area loads as you approach it (two at a time), so you cross with no break. The warehouse and the rowhouse beside it are joined this way.

- `Corridor_01`, the tutorial: a corridor with an open yard at each end under a night sky. On-screen prompts teach the controls in the start yard, then the rules in the first stretch of the corridor. Two monsters patrol it. It ends at a marker in the far yard: the game freezes on "YOU MADE IT" for 2 seconds, then the warehouse loads.
- `Warehouse_01`: a two-storey warehouse with four monsters, stairs up to a catwalk, light from skylights and windows, and an old wooden front door you can open, which shuts behind you. The back door is blocked; the way on is out of an upper window.
- `Rowhouse_01`: the two-storey rowhouse joined to the warehouse's left wall. It loads while you are on the warehouse's upper level, so you jump out of the window onto its roof with no break, then go down through two floors past two monsters to its back door, where the level ends and you can restart at the warehouse.

Play starts in the corridor and carries on into the warehouse, with fresh lives, then into the rowhouse. You can also start in any level.

Working so far:
- Third-person player, a realistic human in a long coat: walk, sprint, crouch and jump, with a camera that stays out of walls.
- Lamp pools that are safe unless a monster is already chasing you, and an on-screen bar showing how exposed you are in the dark.
- Footsteps, landings and kickable cans that make noise enemies can hear.
- The Wick, the monster: it patrols, sees, hears, warns, investigates, chases and searches, and only enters lamp light when already chasing you. A seam on its body changes colour with its mood.
- Hiding spots that end a chase if the monster doesn't see you get in.
- A dim flashlight that points where you look. Holding F makes it brighter, but makes you easier to spot.
- Doors you can open and close. A shut door blocks a monster's view, and monsters never follow you out of the warehouse.
- Getting caught: lives, checkpoints, one punch and one dodge.

See [PLAN.md](PLAN.md) for what's next.

## Controls

| Action | Keyboard and mouse |
|---|---|
| Move | WASD |
| Look | Mouse |
| Sprint | Q |
| Crouch (hold) | Left Shift |
| Jump | Space |
| Bright flashlight (hold) | F |
| Hide, leave hiding spot, open or close a door, restart | E |
| Punch | Left mouse |
| Dodge | Right mouse |

Controller buttons are placeholders and haven't been tested.

## Getting started

1. Install Unity 6000.0.85f1 through Unity Hub.
2. Clone this repository.
3. In Unity Hub, choose **Add project from disk** and select the cloned folder.
4. Open `Assets/Scenes/Main` and press **Play** to play from the start. Press **Esc** to free the mouse.

Pressing **Play** with a level open, such as `Corridor_01` or `Warehouse_01`, starts the game in that level. On its own, a level has no camera, moonlight or colour grading; to edit it with the game's look, also open `Main` (right-click it > **Open Scene Additive**).

On Linux with Intel graphics, launch the editor with `-force-vulkan`: OpenGL draws red noise.

To run the tests, open **Window > General > Test Runner**, choose **EditMode** and click **Run All**.

The project uses the Universal Render Pipeline (URP). See [PLAN.md](PLAN.md) for the plan and project rules.
