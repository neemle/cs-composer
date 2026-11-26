using Neemle.CSharpComposer.Model;
using Neemle.CSharpComposer.Validation;
using Xunit;

namespace Neemle.CSharpComposer.Tests;

// One row per validator rule that builder input can reach: the rule must report its own message,
// and the matching valid form must not.
public sealed class ValidatorRuleContractTests
{
    static CsDocument InMethod(CsLanguageVersion version, params CsStatement[] statements) => new(version)
    {
        Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { new CsMethod(Cs.Void, "M") { Body = Cs.Body(statements) } } } }
    };

    static IReadOnlyList<GenDiagnostic> Statements(params CsStatement[] statements) => InMethod(CsLanguageVersion.CSharp15, statements).Validate();

    static IReadOnlyList<GenDiagnostic> Value(CsExpression expression) =>
        Statements(Cs.Local(Cs.Type("object"), "x", expression));

    static CsExpression Node(ExpressionKind kind, params CsExpression[] arguments) => new(kind, "", arguments);

    static CsExpression Argument(CsExpression value, ArgumentModifier modifier = ArgumentModifier.None) =>
        new(ExpressionKind.Argument, "", new[] { value }) { ArgumentModifier = modifier };

    static void Reports(IReadOnlyList<GenDiagnostic> errors, string message) =>
        Assert.Contains(errors, d => d.Message == message);

    static void DoesNotReport(IReadOnlyList<GenDiagnostic> errors, string message) =>
        Assert.DoesNotContain(errors, d => d.Message == message);

    [Fact]
    public void Conditional_access_flag_needs_a_member_or_index()
    {
        Reports(Value(Cs.Id("a") with { ConditionalAccess = true }), "Conditional access needs a member or index.");
        DoesNotReport(Value(Cs.Id("a").Member("b") with { ConditionalAccess = true }), "Conditional access needs a member or index.");
    }

    [Fact]
    public void Construction_initializer_must_be_an_initializer_on_a_construction()
    {
        const string message = "Construction initializer must be an initializer expression.";
        Reports(Value(Node(ExpressionKind.New) with { Type = Cs.Type("C"), Initializer = Cs.Id("x") }), message);
        Reports(Value(Node(ExpressionKind.Invocation, Cs.Id("F")) with { Initializer = Node(ExpressionKind.Initializer) }), message);
        DoesNotReport(Value(Node(ExpressionKind.New) with { Type = Cs.Type("C"), Initializer = Node(ExpressionKind.Initializer) }), message);
    }

    [Fact]
    public void Constructor_invocation_requires_a_constructible_type()
    {
        const string message = "Constructor invocation requires a constructible type.";
        Reports(Value(Node(ExpressionKind.New) with { Type = Cs.Int.Array() }), message);
        Reports(Value(Node(ExpressionKind.New) with { Type = Cs.Int.Pointer() }), message);
        Reports(Value(Node(ExpressionKind.New) with { Type = Cs.Void }), message);
        DoesNotReport(Value(Node(ExpressionKind.New) with { Type = Cs.Type("C") }), message);
    }

    [Fact]
    public void Typed_operators_reject_a_text_payload()
    {
        var errors = Value(new CsExpression(ExpressionKind.Cast, "x", new[] { Cs.Id("a") }) { Type = Cs.Int });
        Assert.Contains(errors, d => d.Code == "GEN065" && d.Message == "This expression has no text payload.");
        DoesNotReport(Value(new CsExpression(ExpressionKind.Cast, "", new[] { Cs.Id("a") }) { Type = Cs.Int }), "This expression has no text payload.");
    }

    [Theory]
    [InlineData(ExpressionKind.PostIncrement)]
    [InlineData(ExpressionKind.PreDecrement)]
    public void Conditional_access_targets_cannot_be_mutated(ExpressionKind kind)
    {
        const string message = "Conditional access cannot be incremented or decremented.";
        var conditional = Cs.Id("a").Member("b") with { ConditionalAccess = true };
        Reports(Statements(Cs.Statement(Node(kind, conditional.Member("c")))), message);
        DoesNotReport(Statements(Cs.Statement(Node(kind, Cs.Id("a").Member("b")))), message);
        DoesNotReport(Statements(Cs.Statement(Node(kind, Node(ExpressionKind.Invocation)))), message);
    }

    [Fact]
    public void Conditional_target_search_follows_a_chain_of_any_length()
    {
        // GENE-19 D10: a member chain is one nesting level, so the search walks all of it to the conditional access.
        CsExpression chain = Cs.Id("a").Member("b") with { ConditionalAccess = true };
        for (int i = 0; i < 140; i++) chain = chain.Member("m");
        Reports(Statements(Cs.Statement(Cs.PostIncrement(chain))), "Conditional access cannot be incremented or decremented.");
    }

    [Fact]
    public void Argument_modifiers_and_type_arguments_belong_to_their_own_kinds()
    {
        Reports(Value(Cs.Id("a") with { ArgumentModifier = ArgumentModifier.Ref }), "Argument modifier belongs to an argument expression.");
        Reports(Value(Cs.Literal(1) with { TypeArguments = new[] { Cs.Int } }), "Generic type arguments require a name reference.");
        DoesNotReport(Value(Cs.Id("F") with { TypeArguments = new[] { Cs.Int } }), "Generic type arguments require a name reference.");
    }

    [Fact]
    public void Argument_expressions_need_a_call_like_parent_and_tuple_elements_take_no_modifier()
    {
        Reports(Value(Argument(Cs.Id("a"))), "Argument requires an invocation, construction, index or tuple.");
        Reports(Value(Node(ExpressionKind.Tuple, Argument(Cs.Id("a"), ArgumentModifier.Ref), Cs.Id("b"))), "Tuple elements cannot have argument modifiers.");
        var call = Node(ExpressionKind.Invocation, Cs.Id("F"), Argument(Cs.Id("a"), ArgumentModifier.Ref));
        var errors = Value(call);
        DoesNotReport(errors, "Argument requires an invocation, construction, index or tuple.");
        DoesNotReport(errors, "Tuple elements cannot have argument modifiers.");
    }

    [Fact]
    public void Stack_allocation_requires_a_size_or_initializer()
    {
        const string message = "Stack allocation requires a size or initializer.";
        Reports(Value(Node(ExpressionKind.StackAlloc) with { Type = Cs.Int }), message);
        Reports(Value(Node(ExpressionKind.StackAlloc, Cs.Literal(1), Cs.Literal(2)) with { Type = Cs.Int }), message);
        Reports(Value(Node(ExpressionKind.StackAlloc, Cs.Literal(1))), message);
        DoesNotReport(Value(Node(ExpressionKind.StackAlloc, Cs.Literal(1)) with { Type = Cs.Int }), message);
    }

    static CsQueryClause From => new(QueryClauseKind.From) { Name = "x", Value = Cs.Id("xs") };
    static CsQueryClause Select => new(QueryClauseKind.Select) { Value = Cs.Id("x") };

    [Fact]
    public void Query_clauses_after_the_terminal_clause_are_out_of_order()
    {
        const string message = "Query clauses are out of order.";
        Reports(Value(Node(ExpressionKind.Query) with { QueryClauses = new[] { From, Select, new CsQueryClause(QueryClauseKind.Where) { Value = Cs.Literal(true) } } }), message);
        Reports(Value(Node(ExpressionKind.Query) with { QueryClauses = new[] { From, new CsQueryClause(QueryClauseKind.Into) { Name = "g", Clauses = new[] { Select } }, Select } }), message);
        DoesNotReport(Value(Node(ExpressionKind.Query) with { QueryClauses = new[] { From, Select } }), message);
    }

    static CsStatement For(CsForInitializer initializer) => Cs.For(initializer, null, Values<CsExpression>.Empty, Cs.Body());

    [Theory]
    [InlineData(CsVariableModifiers.ReadOnly, false)]
    [InlineData((CsVariableModifiers)64, false)]
    [InlineData(CsVariableModifiers.Const, false)]
    [InlineData(CsVariableModifiers.Ref | CsVariableModifiers.ReadOnly, true)]
    [InlineData(CsVariableModifiers.Scoped, true)]
    public void For_declaration_modifiers_follow_the_local_rules(CsVariableModifiers modifiers, bool valid)
    {
        var errors = Statements(For(Cs.ForDeclaration(Cs.Int, Cs.Variable("i", Cs.Literal(0))) with { VariableModifiers = modifiers }));
        Assert.Equal(!valid, errors.Any(d => d.Message == "Incompatible variable modifiers."));
    }

    [Fact]
    public void Const_locals_cannot_be_inferred()
    {
        var local = new CsStatement(StatementKind.Local, Cs.Literal(1), "x") { IsVar = true, VariableModifiers = CsVariableModifiers.Const };
        Reports(Statements(local), "Incompatible variable modifiers.");
        DoesNotReport(Statements(local with { IsVar = false, Type = Cs.Int }), "Incompatible variable modifiers.");
    }

    [Fact]
    public void Inferred_for_lambda_variables_need_natural_lambda_types()
    {
        var loop = For(Cs.ForVar("f", Cs.Lambda(Cs.Literal(1))));
        Assert.Contains(InMethod(CsLanguageVersion.CSharp9, loop).Validate(), d => d.Message == "Natural lambda type inference requires C# 10.");
        Assert.DoesNotContain(InMethod(CsLanguageVersion.CSharp10, loop).Validate(), d => d.Code == "GEN073");
    }

    static CsStatement Switch(CsBody body) => Cs.Switch(Cs.Id("v"), Cs.Section(body, Cs.Default()));

    [Fact]
    public void Switch_sections_need_statement_lists_and_reachability_is_conservative()
    {
        Reports(Statements(Switch(CsBody.FromExpression(Cs.Literal(1)))), "Switch sections require a statement list.");
        var invalidTree = Statements(Switch(new CsBody { Tree = Cs.Tree(Cs.Symbol("refOnly")) }));
        Assert.Contains(invalidTree, d => d.Code == "SYM001");
        Assert.DoesNotContain(invalidTree, d => d.Code == "GEN081");
    }

    public static IEnumerable<object[]> SectionFlows()
    {
        var e = Cs.Id("e");
        // section body, falls through
        yield return new object[] { Cs.Body(Cs.If(Cs.Literal(true), Cs.Body(Cs.Break()))), false };
        yield return new object[] { Cs.Body(Cs.If(Cs.Literal(false), Cs.Body(Cs.Statement(Cs.Id("F").Call())), Cs.Block(Cs.Break()))), false };
        yield return new object[] { Cs.Body(Cs.If(Cs.Id("b"), Cs.Body(Cs.Break()))), true };
        yield return new object[] { Cs.Body(Cs.Try(Cs.Body(), finallyBody: Cs.Body(Cs.Throw(e)))), false };
        yield return new object[] { Cs.Body(Cs.Try(Cs.Body(Cs.Break()), finallyBody: Cs.Body())), false };
        yield return new object[] { Cs.Body(Cs.Try(Cs.Body(), finallyBody: Cs.Body())), true };
        yield return new object[] { Cs.Body(Cs.Try(Cs.Body(Cs.Break()), new[] { Cs.CatchAll(Cs.Body(Cs.Break())) })), false };
        yield return new object[] { Cs.Body(Cs.Try(Cs.Body(Cs.Break()), new[] { Cs.CatchAll(Cs.Body()) })), true };
    }

    [Theory]
    [MemberData(nameof(SectionFlows))]
    public void Switch_section_fallthrough_follows_constant_conditions_and_finally_blocks(CsBody body, bool fallsThrough)
    {
        var errors = Statements(Switch(body));
        Assert.Equal(fallsThrough, errors.Any(d => d.Code == "GEN081"));
    }

    [Fact]
    public void Unanalyzable_section_statements_never_prove_fallthrough()
    {
        var missingCatches = new CsStatement(StatementKind.Try) { Body = Cs.Body(), Catches = null! };
        var errors = Statements(Switch(Cs.Body(missingCatches)));
        Assert.Contains(errors, d => d.Code == "GEN002");
        Assert.DoesNotContain(errors, d => d.Code == "GEN081");
        var nullHandler = new CsStatement(StatementKind.Try) { Body = Cs.Body(), Catches = new CsCatch[] { null! } };
        Assert.DoesNotContain(Statements(Switch(Cs.Body(nullHandler))), d => d.Code == "GEN081");
        var labelled = Cs.Break() with { Text = "outer" };
        Assert.DoesNotContain(Statements(Switch(Cs.Body(labelled))), d => d.Code == "GEN081");
        Assert.DoesNotContain(Statements(Switch(Cs.Body(Cs.Continue() with { Text = "outer" }))), d => d.Code == "GEN081");
        Assert.DoesNotContain(Statements(Switch(Cs.Body(new CsStatement[] { null! }))), d => d.Code == "GEN081");
    }

    [Fact]
    public void Top_level_statement_trees_report_symbol_model_errors()
    {
        var errors = new CsDocument { Items = new CsNode[] { new CsGlobalStatement(Cs.Symbol("refOnly")) } }.Validate();
        Assert.Contains(errors, d => d.Code == "SYM001");
        Assert.Empty(new CsDocument { Items = new CsNode[] { new CsGlobalStatement(Cs.Instruction(InstructionKind.Return)) } }.Validate());
    }

    [Fact]
    public void Namespace_extern_aliases_need_valid_identifiers()
    {
        static IReadOnlyList<GenDiagnostic> Alias(string name) =>
            new CsDocument { Items = new CsNode[] { new CsNamespace("N", new CsNode[] { new CsExternAlias(name) }) } }.Validate();
        Assert.Contains(Alias("bad name"), d => d.Code == "GEN003" && d.Path.EndsWith(".ExternAlias", StringComparison.Ordinal));
        Assert.Empty(Alias("Lib"));
    }

    [Fact]
    public void A_body_tree_cannot_coexist_with_statements()
    {
        var body = new CsBody(Cs.Return()) { Tree = Cs.Tree(Cs.Instruction(InstructionKind.Return)) };
        Reports(Statements(Cs.Block() with { Body = body }), "A body tree cannot coexist with another body representation.");
        DoesNotReport(Statements(Cs.Block() with { Body = body with { Statements = Values<CsStatement>.Empty } }), "A body tree cannot coexist with another body representation.");
    }

    [Fact]
    public void Using_declarations_need_an_initialized_local()
    {
        const string message = "Using declaration needs an initialized local.";
        Reports(Statements(new CsStatement(StatementKind.UsingDeclaration) { Resource = Cs.Local(Cs.Type("R"), "r") }), message);
        Reports(Statements(new CsStatement(StatementKind.UsingDeclaration) { Resource = Cs.Return() }), message);
        DoesNotReport(Statements(new CsStatement(StatementKind.UsingDeclaration) { Resource = Cs.Local(Cs.Type("R"), "r", Cs.Id("Open").Call()) }), message);
    }

    [Fact]
    public void Statement_section_and_iterator_lists_are_required()
    {
        var errors = Statements(Cs.Empty() with { Sections = null!, Iterators = null! });
        Assert.Contains(errors, d => d.Code == "GEN002" && d.Path.EndsWith(".Sections", StringComparison.Ordinal));
        Assert.Contains(errors, d => d.Code == "GEN002" && d.Path.EndsWith(".Iterators", StringComparison.Ordinal));
    }

    [Fact]
    public void Bodiless_operators_must_be_abstract_or_extern()
    {
        var parameters = new[] { new CsParameter(Cs.Type("C"), "a"), new CsParameter(Cs.Type("C"), "b") };
        CsDocument With(params string[] modifiers) => new()
        {
            Types = new[] { new CsTypeDeclaration(TypeKind.Class, "C") { Members = new CsMember[] { new CsOperatorDeclaration(OverloadableOperator.Plus, Cs.Type("C")) { Modifiers = modifiers, Parameters = parameters } } } }
        };
        Assert.Contains(With("public", "static").Validate(), d => d.Code == "GEN002" && d.Path.EndsWith(".Body", StringComparison.Ordinal));
        Assert.DoesNotContain(With("public", "static", "extern").Validate(), d => d.Code == "GEN002");
    }

    [Fact]
    public void Tuple_designations_accept_only_nested_symbols()
    {
        static CsExpression Is(params CsNode[] parts) => new(ExpressionKind.Is, "", new[] { Cs.Id("o") })
        {
            Pattern = new CsPattern(PatternKind.Var) { Designation = Cs.Symbol(new CsSymbolOptions { Operation = SymbolOperation.Tuple, Parts = parts }) }
        };
        Reports(Value(Is(Cs.Symbol("a"), Cs.Id("b"))), "Designation requires nested symbols.");
        DoesNotReport(Value(Is(Cs.Symbol("a"), Cs.Symbol("b"))), "Designation requires nested symbols.");
    }

    [Fact]
    public void Graph_rules_reject_shared_parsed_nodes_foreign_items_and_non_leaf_comments()
    {
        var parsed = Cs.Parse("class C { int F; }").Root;
        var type = parsed.Types[0];
        var shared = parsed with { Types = new[] { type with { Members = new[] { type.Members[0], type.Members[0] } } } };
        Assert.Contains(shared.Validate(), d => d.Code == "GEN074");
        Assert.Empty(parsed.Validate());

        Assert.Contains(new CsDocument { Items = new CsNode[] { Cs.Id("x") } }.Validate(), d => d.Message == "Invalid document child kind.");
        Assert.Contains(new CsDocument { Items = new CsNode[] { new CsNamespace("N", new CsNode[] { new CsGlobalStatement(Cs.Instruction(InstructionKind.Return)) }) } }.Validate(),
            d => d.Message == "Invalid namespace child kind.");

        var nested = new CsComment("x") { Comments = new[] { new CsComment("y") } };
        var documented = new CsComment("x") { Documentation = new CsXmlNode(XmlNodeKind.Fragment) };
        foreach (var comment in new[] { nested, documented })
            Assert.Contains((parsed with { Comments = new[] { comment } }).Validate(), d => d.Message == "Comment nodes are leaves.");
        var documentation = new CsComment("/// x", CommentKind.Documentation) { Documentation = new CsXmlNode(XmlNodeKind.Fragment) };
        Assert.DoesNotContain((parsed with { Comments = new[] { documentation } }).Validate(), d => d.Message == "Comment nodes are leaves.");
    }

    [Fact]
    public void Instruction_trees_lower_blocks_expression_initializers_and_reject_unknown_kinds()
    {
        var block = Cs.Tree(Cs.Instruction(InstructionKind.Block, new() { Body = Cs.Tree(Cs.Instruction(InstructionKind.Return)) }));
        Assert.Empty(block.Validate());
        Assert.Equal("{\n    return;\n}", block.Render().Replace("\r\n", "\n").Trim());

        Assert.Contains(Cs.Tree(Cs.Instruction((InstructionKind)99)).Validate(), d => d.Code == "SYM001" && d.Message == "Unknown instruction kind.");

        static CsSymbol Assign(string name, int value) => Cs.Symbol(name, new() { AsAssignment = true, Value = Cs.Value(value) });
        var single = Cs.Tree(Cs.Instruction(InstructionKind.For, new() { Initializer = Assign("i", 0), Body = Cs.Tree(Cs.Instruction(InstructionKind.Break)) }));
        Assert.Empty(single.Validate());
        Assert.StartsWith("for (i = 0; ; )", single.Render());
        var compound = Cs.Tree(Cs.Instruction(InstructionKind.For, new() { Initializer = Cs.Compound(Assign("i", 0), Assign("j", 1)), Body = Cs.Tree(Cs.Instruction(InstructionKind.Break)) }));
        Assert.Empty(compound.Validate());
        Assert.StartsWith("for (i = 0, j = 1; ; )", compound.Render());
    }

    static CsExpression Is(CsPattern pattern) => new(ExpressionKind.Is, "", new[] { Cs.Id("o") }) { Pattern = pattern };

    static CsSymbol TupleDesignation => Cs.Symbol(new CsSymbolOptions { Operation = SymbolOperation.Tuple, Parts = new CsNode[] { Cs.Symbol("a"), Cs.Symbol("b") } });

    public static IEnumerable<object[]> PatternShapes()
    {
        var one = Cs.ConstantPattern(Cs.Literal(1));
        yield return new object[] { new CsPattern(PatternKind.Var) { Name = "x", Designation = TupleDesignation }, "Compound designation belongs to a var pattern." };
        yield return new object[] { new CsPattern(PatternKind.Slice), "Slice belongs only to a list pattern." };
        yield return new object[] { new CsPattern(PatternKind.List) { Patterns = new[] { new CsPattern(PatternKind.Slice) { Patterns = new[] { one, one } } } }, "Slice permits at most one nested pattern." };
        yield return new object[] { one with { HasPositional = true }, "Recursive clauses belong to recursive patterns." };
        yield return new object[] { new CsPattern(PatternKind.Recursive) { Type = Cs.Type("P") }, "Recursive pattern needs positional or property clauses." };
        yield return new object[] { new CsPattern(PatternKind.Recursive) { HasPositional = true, Patterns = new[] { one }, Properties = new[] { new CsPattern(PatternKind.Property) { Name = "A", Patterns = new[] { one } } } }, "Property clause is absent." };
        yield return new object[] { new CsPattern(PatternKind.Recursive) { HasProperties = true, Properties = new[] { one } }, "Property clause requires named subpatterns." };
    }

    [Theory]
    [MemberData(nameof(PatternShapes))]
    public void Pattern_shapes_report_their_own_rule(CsPattern pattern, string message) => Reports(Value(Is(pattern)), message);

    [Fact]
    public void Well_formed_pattern_shapes_pass_the_same_rules()
    {
        var one = Cs.ConstantPattern(Cs.Literal(1));
        var errors = Value(Is(new CsPattern(PatternKind.List) { Patterns = new[] { one, new CsPattern(PatternKind.Slice) } }));
        Assert.DoesNotContain(errors, d => d.Message.Contains("Slice", StringComparison.Ordinal));
        var property = Value(Is(new CsPattern(PatternKind.Recursive) { HasProperties = true, Properties = new[] { new CsPattern(PatternKind.Property) { Name = "A", Patterns = new[] { one } } } }));
        Assert.DoesNotContain(property, d => d.Message.Contains("clause", StringComparison.Ordinal));
        Assert.DoesNotContain(Value(Is(new CsPattern(PatternKind.Var) { Designation = TupleDesignation })), d => d.Message == "Compound designation belongs to a var pattern.");
    }
}
