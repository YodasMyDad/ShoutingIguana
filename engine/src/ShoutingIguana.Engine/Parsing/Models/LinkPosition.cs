namespace ShoutingIguana.Engine.Parsing.Models;

/// <summary>Where on the page a link sits, from semantic elements, ARIA roles and class/id hints.</summary>
public enum LinkPosition : byte
{
    Content,
    Navigation,
    Header,
    Footer,
    Sidebar,
    Head
}
