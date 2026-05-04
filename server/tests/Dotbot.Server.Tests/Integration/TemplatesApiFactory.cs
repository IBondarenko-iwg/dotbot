using Dotbot.Server.Models;
using Dotbot.Server.Services;
using Microsoft.Agents.Core.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dotbot.Server.Tests.Integration;

public sealed class TemplatesApiFactory : WebApplicationFactory<Program>
{
    internal const string TestApiKey = "integration-test-key-abc123";

    public InMemoryTemplateStorage Storage { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Provide minimum required configuration so the host boots without Azure resources.
        builder.UseSetting("BlobStorage:ConnectionString", "UseDevelopmentStorage=true");
        builder.UseSetting("ApiSecurity:ApiKey", TestApiKey);

        // Stub required Auth config so JwtSigningKeyProvider and MagicLinkService don't fail to resolve.
        builder.UseSetting("Auth:JwtSigningKey", "integration-test-signing-key-32-chars!!");
        builder.UseSetting("Auth:JwtIssuer", "dotbot-test");
        builder.UseSetting("Auth:JwtAudience", "dotbot-test");

        builder.ConfigureServices(services =>
        {
            // Replace the three DI-blocking services with in-process test doubles.
            services.RemoveAll<ITemplateStorageService>();
            services.RemoveAll<IAdministratorService>();
            services.RemoveAll<IConversationReferenceStore>();

            services.AddSingleton<ITemplateStorageService>(Storage);
            services.AddSingleton<IAdministratorService>(new NullAdministratorService());
            services.AddSingleton<IConversationReferenceStore>(new NullConversationReferenceStore());
        });
    }
}

public sealed class InMemoryTemplateStorage : ITemplateStorageService
{
    private readonly List<QuestionTemplate> _saved = [];

    public IReadOnlyList<QuestionTemplate> Saved => _saved;

    public Task SaveTemplateAsync(QuestionTemplate template)
    {
        _saved.Add(template);
        return Task.CompletedTask;
    }

    public Task<QuestionTemplate?> GetTemplateAsync(string projectId, Guid questionId, int version)
        => Task.FromResult(_saved.FirstOrDefault(x =>
            x.Project.ProjectId == projectId
            && x.QuestionId == questionId
            && x.Version == version));
}

internal sealed class NullAdministratorService : IAdministratorService
{
    public Task SeedIfEmptyAsync() => Task.CompletedTask;
    public Task<bool> IsAdministratorAsync(string email) => Task.FromResult(false);
}

internal sealed class NullConversationReferenceStore : IConversationReferenceStore
{
    public Task LoadAsync() => Task.CompletedTask;
    public void AddOrUpdate(string userObjectId, ConversationReference reference) { }
    public ConversationReference? Get(string userObjectId) => null;
}
