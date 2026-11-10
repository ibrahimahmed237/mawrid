var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();

/// <summary>Exposed so integration tests can start the app with WebApplicationFactory.</summary>
public partial class Program;
