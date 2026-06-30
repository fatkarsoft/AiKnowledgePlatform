using AiKnowledgePlatform.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication(builder.Configuration);

var app = builder.Build();

app.MapEndpoints();

app.Run();

public partial class Program;
