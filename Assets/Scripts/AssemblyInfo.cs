using System.Runtime.CompilerServices;

// Lets edit mode tests reach internal game logic, such as the detection maths.
[assembly: InternalsVisibleTo("LastLamp.Tests")]

// Lets one-off editor tools time animations to the enemy's rules, such as the grab's wind-up.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
