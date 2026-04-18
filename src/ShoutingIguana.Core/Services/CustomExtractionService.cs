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
    public async Task<List<CustomExtractionRule>> GetRulesByProjectIdAsync(int projectId)
    {
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomExtractionRuleRepository>();
        return await repository.GetByProjectIdAsync(projectId).ConfigureAwait(false);
    }

    public async Task<CustomExtractionRule?> GetRuleAsync(int ruleId)
    {
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomExtractionRuleRepository>();
        return await repository.GetByIdAsync(ruleId).ConfigureAwait(false);
    }

    public async Task<CustomExtractionRule> SaveRuleAsync(CustomExtractionRule rule)
    {
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomExtractionRuleRepository>();
        
        if (rule.Id == 0)
        {
            logger.LogInformation("Creating new extraction rule: {RuleName} for project {ProjectId}", 
                rule.Name, rule.ProjectId);
            return await repository.CreateAsync(rule).ConfigureAwait(false);
        }
        else
        {
            logger.LogInformation("Updating extraction rule: {RuleName} (ID: {RuleId})", 
                rule.Name, rule.Id);
            await repository.UpdateAsync(rule).ConfigureAwait(false);
            return rule;
        }
    }

    public async Task DeleteRuleAsync(int ruleId)
    {
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICustomExtractionRuleRepository>();
        
        logger.LogInformation("Deleting extraction rule ID: {RuleId}", ruleId);
        await repository.DeleteAsync(ruleId).ConfigureAwait(false);
    }
}

