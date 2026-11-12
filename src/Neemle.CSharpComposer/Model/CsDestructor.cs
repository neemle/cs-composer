namespace Neemle.CSharpComposer.Model;

public sealed record CsDestructor(string Name, CsBody? Body) : CsMember(Name);
