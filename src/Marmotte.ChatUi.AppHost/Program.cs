var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Marmotte_ChatUi_Web>("web");

builder.Build().Run();
