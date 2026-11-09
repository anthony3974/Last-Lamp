# Last Lamp

A noir horror stealth game made with Unity. You cross the dark while monsters hunt you. They fear light, so every lamp is a safe place until one of them is already chasing you, and every stretch between lamps is a risk.

Sneaking comes first, and combat is only the backup. Stealth should always be a real option: enemies give a warning before they spot you, and you can always see how exposed you are.

PC is the main platform. Mobile may come later.

## Current state

Early graybox prototype: one level, built from plain shapes, with a corridor and an open yard at each end under a night sky.

Working so far:
- Third-person player: walk, sprint, crouch and jump, with a camera that stays out of walls.
- Lamp pools that are safe unless a monster is already chasing you, and an on-screen bar showing how exposed you are in the dark.
- Footsteps, landings and kickable cans that make noise enemies can hear.
- Two monsters that patrol, see, hear, warn, investigate, chase and search, and only enter lamp light when already chasing you.
- Two hiding spots that end a chase if the monster doesn't see you get in.
- A dim flashlight that points where you look.
- Getting caught: lives, checkpoints, one punch and one dodge.

The level ends at a marker in the far yard, where you can restart. See [PLAN.md](PLAN.md) for what's next.

## Controls

| Action | Keyboard and mouse |
|---|---|
| Move | WASD |
| Look | Mouse |
| Sprint | Q |
| Crouch (hold) | Left Shift |
| Jump | Space |
| Hide, leave hiding spot, restart | E |
| Punch | Left mouse |
| Dodge | Right mouse |

Controller buttons are placeholders and haven't been tested.

## Getting started

1. Install Unity 6000.0.85f1 through Unity Hub.
2. Clone this repository.
3. In Unity Hub, choose **Add project from disk** and select the cloned folder.
4. Open `Assets/Scenes/Corridor_01` and press **Play**. Press **Esc** to free the mouse.

To run the tests, open **Window > General > Test Runner**, choose **EditMode** and click **Run All**.

The project uses the Universal Render Pipeline (URP). See [PLAN.md](PLAN.md) for the plan and project rules.
