using Elsa.EntityFrameworkCore.Common;
using Elsa.EntityFrameworkCore.Extensions;
using Elsa.EntityFrameworkCore.Modules.Management;
using Elsa.EntityFrameworkCore.Modules.Runtime;
using Elsa.Extensions;
using Medallion.Threading.FileSystem;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHealthChecks();
builder.Services
        .AddElsa(elsa =>
        {
            var dbContextOptions = new ElsaDbContextOptions();
            string postgresConnectionString = configuration.GetConnectionString("ElsaDbPostgres")!;


            elsa.UseWorkflowManagement(management => management.UseEntityFrameworkCore(ef => ef.UsePostgreSql(postgresConnectionString, dbContextOptions)));
            elsa.UseWorkflowRuntime(runtime => runtime.UseEntityFrameworkCore(ef => ef.UsePostgreSql(postgresConnectionString, dbContextOptions)));
            elsa.UseWorkflowRuntime(runtime =>
            {
                runtime.DistributedLockProvider = _ => new FileDistributedSynchronizationProvider(new DirectoryInfo(Path.Combine(Environment.CurrentDirectory, "..//locks")));
            });

            elsa.UseIdentity(identity =>
            {
                identity.TokenOptions = options =>
                {
                    options.SigningKey = "c7dc81876a782d502084763fa322429fca015941eac90ce8ca7ad95fc8752035";
                    options.AccessTokenLifetime = TimeSpan.FromDays(1);
                };

                identity.UseAdminUserProvider();
            });
            elsa.UseDefaultAuthentication(auth => auth.UseAdminApiKey());
            elsa.UseWorkflowsApi();
            elsa.UseRealTimeWorkflows();
            elsa.UseJavaScript(options => options.AllowClrAccess = true);

            var selfHostingUrl = configuration["SelfHostingUrl"];
            if (selfHostingUrl != null)
            {
                elsa.UseHttp(http => http.ConfigureHttpOptions = options =>
                {
                    options.BaseUrl = new Uri(selfHostingUrl);
                    options.BasePath = "/workflows";
                });
            }

            elsa.UseWebhooks(webhooks => webhooks.WebhookOptions = options => builder.Configuration.GetSection("Webhooks").Bind(options));
        });

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        var result = report.Status == HealthStatus.Healthy
            ? "Healthy"
            : $"Unhealthy: {report.Status}";

        await context.Response.WriteAsync(result);
    }
});

app.UseHttpsRedirection();
app.UseCors(c =>
    c.AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("x-elsa-workflow-instance-id")
);
app.UseAuthorization();
app.UseWorkflows();

app.MapControllers();
app.UseWorkflowsApi();

app.Run();