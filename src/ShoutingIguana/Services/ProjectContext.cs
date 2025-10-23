using System;

namespace ShoutingIguana.Services;

public class ProjectContext : IProjectContext
{
    public string? CurrentProjectPath { get; private set; }
    public int? CurrentProjectId { get; private set; }
    public string? CurrentProjectName { get; private set; }
    public bool HasOpenProject => CurrentProjectId.HasValue;

    public event EventHandler? ProjectChanged;

    public void OpenProject(string projectPath, int projectId, string projectName)
    {
        CurrentProjectPath = projectPath;
        CurrentProjectId = projectId;
        CurrentProjectName = projectName;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CloseProject()
    {
        CurrentProjectPath = null;
        CurrentProjectId = null;
        CurrentProjectName = null;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }
}

