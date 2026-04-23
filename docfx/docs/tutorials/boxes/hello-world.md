# Hello World

We will now add physics to the scene. We do this by creating a new instance of the World class and adding several rigid bodies to it.

Replace the content of `Program.cs` with the following code (highlighted lines indicate the additions to the source code):

[!code-csharp[](snippets/hello-world.cs?highlight=3-6,9-18,28,41-63,88-94)]

Running your program, you should now see a few boxes dynamically falling onto the ground.

![plane](images/raylibjitter.gif)
