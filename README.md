# Last Lamp

A noir horror stealth game made with Unity. You cross the dark while monsters hunt you. They fear light, so every lamp is a safe place until one of them is already chasing you, and every stretch between lamps is a risk.

Sneaking comes first, and combat is only the backup. Stealth should always be a real option: enemies give a warning before they spot you, and you can always see how exposed you are.

PC is the main platform. Mobile may come later.

## Current state

Early prototype with two levels. The levels are still mostly plain shapes, but the player and the monster are finished models.

- `Corridor_01`, the tutorial: a corridor with an open yard at each end under a night sky. On-screen prompts teach the controls in the start yard, then the rules in the first stretch of the corridor. Two monsters patrol it. It ends at a marker in the far yard, where you can restart.
- `Warehouse_01`: a two-storey warehouse with four monsters, stairs up to a catwalk, light from skylights and windows, and an old wooden front door you can open. The back door is blocked; for now the level ends at a test marker outside the upper window.

The two levels aren't linked yet, so open each one on its own.

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
4. Open `Assets/Scenes/Corridor_01` (or `Warehouse_01`) and press **Play**. Press **Esc** to free the mouse.

On Linux with Intel graphics, launch the editor with `-force-vulkan`: OpenGL draws red noise.

To run the tests, open **Window > General > Test Runner**, choose **EditMode** and click **Run All**.

The project uses the Universal Render Pipeline (URP). See [PLAN.md](PLAN.md) for the plan and project rules.
