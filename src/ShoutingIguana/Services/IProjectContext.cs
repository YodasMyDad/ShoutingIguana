using System;

namespace ShoutingIguana.Services;

public interface IProjectContext
{
    string? CurrentProjectPath { get; }
    int? CurrentProjectId { get; }
    string? CurrentProjectName { get; }
    bool HasOpenProject { get; }
    
    void OpenProject(string projectPath, int projectId, string projectName);
    void CloseProject();
    
    event EventHandler? ProjectChanged;
}

