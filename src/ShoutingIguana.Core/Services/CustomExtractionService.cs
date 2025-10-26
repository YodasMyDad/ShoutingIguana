using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShoutingIguana.Core.Models;
using ShoutingIguana.Core.Repositories;

namespace ShoutingIguana.Core.Services;

/// <summary>
/// Implementation of ICustomExtractionService.
/// </summary>
public class CustomExtractionService(ILogger<CustomExtractionService> logger, IServiceProvider serviceProvider) : ICustomExtractionService
{
    private readonly ILogger<CustomExtractionService> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public async Task<List<CustomExtractionRule>> GetRulesByProjectIdAsync(int projectId)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomExtractionRuleRepository>();
        return await repository.GetByProjectIdAsync(projectId);
    }

    public async Task<CustomExtractionRule?> GetRuleAsync(int ruleId)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomExtractionRuleRepository>();
        return await repository.GetByIdAsync(ruleId);
    }

    public async Task<CustomExtractionRule> SaveRuleAsync(CustomExtractionRule rule)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomExtractionRuleRepository>();
        
        if (rule.Id == 0)
        {
            _logger.LogInformation("Creating new extraction rule: {RuleName} for project {ProjectId}", 
                rule.Name, rule.ProjectId);
            return await repository.CreateAsync(rule);
        }
        else
        {
            _logger.LogInformation("Updating extraction rule: {RuleName} (ID: {RuleId})", 
                rule.Name, rule.Id);
            await repository.UpdateAsync(rule);
            return rule;
        }
    }

    public async Task DeleteRuleAsync(int ruleId)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomExtractionRuleRepository>();
        
        _logger.LogInformation("Deleting extraction rule ID: {RuleId}", ruleId);
        await repository.DeleteAsync(ruleId);
    }
}

