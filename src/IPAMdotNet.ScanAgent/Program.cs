using IPAMdotNet.ScanAgent;

// Agent de scan distant d'IPAMdotNet : à installer dans un réseau que le serveur ne peut pas joindre.
// Console, service Windows (sc.exe create … binPath= …) ou service systemd (Type=notify).
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "IPAMdotNet.ScanAgent");
builder.Services.AddSystemd();
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.Section));
builder.Services.AddHostedService<AgentWorker>();
builder.Build().Run();
