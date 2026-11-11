using System.Runtime.CompilerServices;

// Lets edit mode tests reach internal game logic, such as the detection maths.
[assembly: InternalsVisibleTo("LastLamp.Tests")]
