using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Neemle.CSharpComposer.Model;
public sealed record CsEnumMember(string Name, CsExpression? Value = null) : CsMember(Name);
