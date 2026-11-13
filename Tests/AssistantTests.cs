using System.Text.Json;
using Application.Assistant;
using Application.Common.Exceptions;
using Domain.Common;
using Domain.Organizations;
using Domain.Users;
using Infrastructure.Assistant;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

internal sealed class ScriptedModel(params ModelTurn[] turns) : IAssistantModel
{
    private readonly Queue<ModelTurn> _turns = new(turns);
    public List<AssistantConversation> Conversations { get; } = [];

    public Task<ModelTurn> GenerateAsync(AssistantConversation conversation, CancellationToken cancellationToken)
    {
        Conversations.Add(conversation);
        return Task.FromResult(_turns.Count > 0 ? _turns.Dequeue() : new ModelTurn("done", [], null));
    }
}

internal sealed class LoopingModel : IAssistantModel
{
    public int CallCount { get; private set; }

    public Task<ModelTurn> GenerateAsync(AssistantConversation conversation, CancellationToken cancellationToken)
    {
        CallCount++;
        var call = new ModelToolCall("get_receivables_aging", new Dictionary<string, JsonElement>(), $"c{CallCount}");
        return Task.FromResult(new ModelTurn(null, [call], null));
    }
}

internal static class AssistantKit
{
    public static IAssistantTool[] Tools(AppDbContext ctx, Seed seed, TimeProvider clock)
    {
        var org = new TestCurrentOrganization(seed.OrganizationId);
        var dashboard = new DashboardRepository(ctx);
        var payables = new PayablesRepository(ctx);
        var receivables = new ReceivablesRepository(ctx);
        var organizations = new OrganizationRepository(ctx);

        return
        [
            new FinancialSummaryTool(new Application.Reports.GetFinancialSummaryHandler(dashboard, payables, organizations, org, clock)),
            new YearComparisonTool(new Application.Reports.GetYearComparisonHandler(dashboard, payables, organizations, org, clock)),
            new ExpenseReportTool(new Application.Reports.GetExpenseReportHandler(payables, organizations, org, clock)),
            new ReceivablesAgingTool(new Application.Reports.GetAgingHandler(receivables, organizations, org, clock)),
            new PayablesAgingTool(new Application.Reports.GetPayablesAgingHandler(payables, organizations, org, clock)),
            new CashFlowForecastTool(new Application.Reports.GetCashFlowForecastHandler(receivables, payables, organizations, org, clock)),
            new OverdueInvoicesTool(new Application.Invoices.ListInvoicesHandler(new InvoiceRepository(ctx), organizations, org, clock)),
            new BillsDueTool(new Application.Bills.ListBillsHandler(new BillRepository(ctx), organizations, org, clock), organizations, org, clock)
        ];
    }

    public static AskAssistantHandler Ask(
        AppDbContext ctx, Seed seed, Guid userId, IAssistantModel model, AssistantSettings settings, TimeProvider clock)
        => new(model, Tools(ctx, seed, clock), new MembershipRepository(ctx), new OrganizationRepository(ctx),
            new TestCurrentOrganization(seed.OrganizationId), new TestCurrentUser(userId), settings, clock);
}

public class AssistantTests
{
    private static readonly FixedTimeProvider Clock = PaymentTestData.Clock; // 15 January 2026

    private static AssistantSettings Enabled() => new() { Enabled = true, ApiKey = "test-key" };

    private static ModelToolCall Call(string name, params (string Key, object Value)[] args)
        => new(name, args.ToDictionary(a => a.Key, a => JsonSerializer.SerializeToElement(a.Value)), "c1");

    private static async Task<Guid> NewMemberAsync(Seed seed, OrganizationRole role)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, seed.UserId);
        var user = User.Register($"{Guid.NewGuid()}@example.com", "hash", "Team Member");
        ctx.Users.Add(user);
        ctx.Memberships.Add(Membership.Create(user.Id, seed.OrganizationId, role));
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    private static async Task<AssistantAnswerDto> AskAsync(
        Seed seed, Guid userId, IAssistantModel model, string question, AssistantSettings? settings = null)
    {
        await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, userId);
        return await AssistantKit.Ask(ctx, seed, userId, model, settings ?? Enabled(), Clock)
            .Handle(new AskAssistantCommand(question), CancellationToken.None);
    }

    [Fact]
    public async Task AQuestion_RunsTheToolsAndReturnsTheAnswerWithTheVerifiedFigures()
    {
        var seed = await PaymentTestData.SeedAsync();
        await PaymentTestData.IssueInvoiceAsync(seed); // 253.97 net income
        var owner = await NewMemberAsync(seed, OrganizationRole.Owner);
        var model = new ScriptedModel(
            new ModelTurn(null, [Call("get_financial_summary", ("from", "2026-01-01"), ("to", "2026-01-31"))], null),
            new ModelTurn("Your estimated profit for January was NGN 253.97.", [], null));

        var answer = await AskAsync(seed, owner, model, "What was my profit in January?");

        Assert.Equal("Your estimated profit for January was NGN 253.97.", answer.Answer);
        Assert.NotEmpty(answer.Notice);

        var source = Assert.Single(answer.Sources);
        Assert.Equal("get_financial_summary", source.Tool);
        Assert.True(source.Succeeded);
        Assert.Equal(253.97m, source.Data.GetProperty("period").GetProperty("income").GetDecimal());

        // The model was given the real figures, and told the rules and today's date.
        Assert.Equal(2, model.Conversations.Count);
        Assert.Equal(8, model.Conversations[0].Tools.Count);
        Assert.Contains("2026-01-15", model.Conversations[0].SystemPrompt);
        Assert.Contains("NGN", model.Conversations[0].SystemPrompt);
        Assert.Contains("Never calculate", model.Conversations[0].SystemPrompt);
        Assert.Contains("screenshots and SMS notifications alone do not prove funds arrived", model.Conversations[0].SystemPrompt);
        Assert.Contains("Never promise credit", model.Conversations[0].SystemPrompt);
        Assert.Contains("unpaid invoice is money owed, not cash received", model.Conversations[0].SystemPrompt);

        var step = Assert.Single(model.Conversations[1].Steps);
        Assert.Contains("253.97", step.Responses[0].Result.GetRawText());
    }

    [Fact]
    public async Task AStaffMember_IsOnlyShownTheToolsTheirRoleAllows_AndCannotRunOthers()
    {
        var seed = await PaymentTestData.SeedAsync();
        await PaymentTestData.IssueInvoiceAsync(seed);
        var staff = await NewMemberAsync(seed, OrganizationRole.Staff);

        // The model tries a tool it was never shown.
        var model = new ScriptedModel(
            new ModelTurn(null, [Call("get_financial_summary")], null),
            new ModelTurn("I cannot see that.", [], null));

        var answer = await AskAsync(seed, staff, model, "What is our profit?");

        var shown = Assert.Single(model.Conversations[0].Tools);
        Assert.Equal("list_overdue_invoices", shown.Name);

        var source = Assert.Single(answer.Sources);
        Assert.False(source.Succeeded);
        Assert.True(source.Data.TryGetProperty("error", out _));
        Assert.False(source.Data.TryGetProperty("period", out _));
    }

    [Fact]
    public async Task UnknownToolsAndBadArguments_BecomeErrorsTheModelCanSee()
    {
        var seed = await PaymentTestData.SeedAsync();
        var owner = await NewMemberAsync(seed, OrganizationRole.Owner);
        var model = new ScriptedModel(
            new ModelTurn(null,
                [Call("delete_everything"), Call("get_financial_summary", ("from", "not-a-date"))], null),
            new ModelTurn("Sorry, I could not do that.", [], null));

        var answer = await AskAsync(seed, owner, model, "Do something odd");

        Assert.Equal(2, answer.Sources.Count);
        Assert.All(answer.Sources, s => Assert.False(s.Succeeded));
        Assert.Equal("Sorry, I could not do that.", answer.Answer);

        var responses = model.Conversations[1].Steps[0].Responses;
        Assert.Contains("does not exist", responses[0].Result.GetRawText());
        Assert.Contains("YYYY-MM-DD", responses[1].Result.GetRawText());
    }

    [Fact]
    public async Task AModelThatKeepsCallingTools_IsStoppedAfterTheRoundLimit()
    {
        var seed = await PaymentTestData.SeedAsync();
        var owner = await NewMemberAsync(seed, OrganizationRole.Owner);
        var model = new LoopingModel();

        var answer = await AskAsync(seed, owner, model, "Which customers owe me money?");

        Assert.Equal(4, model.CallCount);           // three rounds of tools, then the fourth call is cut off
        Assert.Equal(3, answer.Sources.Count);
        Assert.Contains("could not", answer.Answer);
    }

    [Fact]
    public async Task ExtraToolCallsInOneStep_AreRefused_ButStillAnswered()
    {
        var seed = await PaymentTestData.SeedAsync();
        var owner = await NewMemberAsync(seed, OrganizationRole.Owner);
        var calls = Enumerable.Range(0, 6).Select(_ => Call("get_receivables_aging")).ToList();
        var model = new ScriptedModel(new ModelTurn(null, calls, null), new ModelTurn("Done.", [], null));

        var answer = await AskAsync(seed, owner, model, "Check everything");

        Assert.Equal(6, model.Conversations[1].Steps[0].Responses.Count);
        Assert.Equal(6, answer.Sources.Count);
        Assert.Equal(4, answer.Sources.Count(s => s.Succeeded));
    }

    [Fact]
    public async Task BlankOrOverlongQuestions_AreRejected_BeforeTheModelIsCalled()
    {
        var seed = await PaymentTestData.SeedAsync();
        var owner = await NewMemberAsync(seed, OrganizationRole.Owner);
        var model = new ScriptedModel();

        await Assert.ThrowsAsync<DomainException>(() => AskAsync(seed, owner, model, "   "));
        await Assert.ThrowsAsync<DomainException>(() => AskAsync(seed, owner, model, new string('x', 501)));

        Assert.Empty(model.Conversations);
    }

    [Fact]
    public async Task WhenTheAssistantIsSwitchedOff_NothingIsSent()
    {
        var seed = await PaymentTestData.SeedAsync();
        var owner = await NewMemberAsync(seed, OrganizationRole.Owner);
        var model = new ScriptedModel();

        await Assert.ThrowsAsync<AssistantUnavailableException>(
            () => AskAsync(seed, owner, model, "Hello?", new AssistantSettings { Enabled = false }));

        Assert.Empty(model.Conversations);
    }

    [Fact]
    public async Task AUserWhoIsNotAMember_IsRefused()
    {
        var seed = await PaymentTestData.SeedAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => AskAsync(seed, seed.UserId, new ScriptedModel(), "Hello?"));
    }

    [Fact]
    public async Task TheAssistant_NeverSeesAnotherOrganizationsNumbers()
    {
        var seedA = await PaymentTestData.SeedAsync();
        var seedB = await PaymentTestData.SeedAsync();
        await PaymentTestData.IssueInvoiceAsync(seedA);
        var ownerB = await NewMemberAsync(seedB, OrganizationRole.Owner);
        var model = new ScriptedModel(
            new ModelTurn(null, [Call("get_financial_summary", ("from", "2026-01-01"), ("to", "2026-01-31"))], null),
            new ModelTurn("Nothing recorded.", [], null));

        var answer = await AskAsync(seedB, ownerB, model, "What was my income in January?");

        var source = Assert.Single(answer.Sources);
        Assert.Equal(0m, source.Data.GetProperty("period").GetProperty("income").GetDecimal());
    }

    [Fact]
    public async Task ListingTools_ShowsOnlyWhatTheRoleAllows()
    {
        var seed = await PaymentTestData.SeedAsync();
        var owner = await NewMemberAsync(seed, OrganizationRole.Owner);
        var staff = await NewMemberAsync(seed, OrganizationRole.Staff);

        async Task<IReadOnlyList<AssistantToolInfoDto>> ListAsync(Guid userId)
        {
            await using var ctx = TestDatabase.CreateContext(Clock, seed.OrganizationId, userId);
            return await new ListAssistantToolsHandler(
                    AssistantKit.Tools(ctx, seed, Clock), new MembershipRepository(ctx),
                    new TestCurrentOrganization(seed.OrganizationId), new TestCurrentUser(userId))
                .Handle(CancellationToken.None);
        }

        Assert.Equal(8, (await ListAsync(owner)).Count);
        Assert.Equal("list_overdue_invoices", Assert.Single(await ListAsync(staff)).Name);
    }
}

public class ToolArgsTests
{
    private static IReadOnlyDictionary<string, JsonElement> Args(params (string Key, object? Value)[] items)
        => items.ToDictionary(i => i.Key, i => JsonSerializer.SerializeToElement(i.Value));

    [Fact]
    public void Date_ParsesIsoDates_AndTreatsMissingOrBlankValuesAsNothing()
    {
        Assert.Equal(new DateOnly(2026, 1, 31), ToolArgs.Date(Args(("to", "2026-01-31")), "to"));
        Assert.Null(ToolArgs.Date(Args(), "to"));
        Assert.Null(ToolArgs.Date(Args(("to", "  ")), "to"));
        Assert.Null(ToolArgs.Date(Args(("to", null)), "to"));
    }

    [Theory]
    [InlineData("15/01/2026")]
    [InlineData("2026-13-01")]
    [InlineData("yesterday")]
    public void Date_RejectsAnythingThatIsNotAnIsoDate(string value)
        => Assert.Throws<DomainException>(() => ToolArgs.Date(Args(("from", value)), "from"));

    [Fact]
    public void Integer_AcceptsNumbersAndNumericText_AndRejectsTheRest()
    {
        Assert.Equal(2026, ToolArgs.Integer(Args(("year", 2026)), "year"));
        Assert.Equal(2025, ToolArgs.Integer(Args(("year", "2025")), "year"));
        Assert.Null(ToolArgs.Integer(Args(), "year"));

        Assert.Throws<DomainException>(() => ToolArgs.Integer(Args(("year", "abc")), "year"));
        Assert.Throws<DomainException>(() => ToolArgs.Integer(Args(("year", 12.5)), "year"));
    }
}

public class AssistantSettingsTests
{
    [Fact]
    public void WhenSwitchedOff_NothingIsChecked()
        => new AssistantSettings { Enabled = false }.Validate();

    [Fact]
    public void WhenSwitchedOn_ItNeedsAKey_AndSafeValues()
    {
        Assert.Throws<InvalidOperationException>(() => new AssistantSettings { Enabled = true }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AssistantSettings { Enabled = true, ApiKey = "k", Model = "../evil" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AssistantSettings { Enabled = true, ApiKey = "k", Thinking = "extreme" }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AssistantSettings { Enabled = true, ApiKey = "k", MaxToolRounds = 20 }.Validate());

        new AssistantSettings { Enabled = true, ApiKey = "k" }.Validate();
    }
}

internal sealed class RecordingHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(string Url, string? Key, string Body)> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.FirstOrDefault() : null;
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request.RequestUri!.ToString(), key, body));
        return respond();
    }
}

public class GemmaClientTests
{
    private static AssistantConversation Conversation(IReadOnlyList<AssistantStep>? steps = null) => new(
        "Be helpful.",
        [new ToolDefinition("get_thing", "Gets a thing.",
            [new ToolParameter("from", "STRING", "Start date.", false), new ToolParameter("year", "INTEGER", "Year.", true)])],
        "How much?", steps ?? []);

    private static GemmaAssistantModel Client(RecordingHandler handler) => new(
        new HttpClient(handler), new AssistantSettings { Enabled = true, ApiKey = "test-key" },
        NullLogger<GemmaAssistantModel>.Instance);

    [Fact]
    public void BuildRequest_DeclaresTheSystemPromptToolsQuestionAndThinkingLevel()
    {
        var json = GemmaAssistantModel.BuildRequest(Conversation(), "minimal");

        Assert.Equal("Be helpful.", json["systemInstruction"]!["parts"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("user", json["contents"]![0]!["role"]!.GetValue<string>());
        Assert.Equal("How much?", json["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>());

        var declaration = json["tools"]![0]!["functionDeclarations"]![0]!;
        Assert.Equal("get_thing", declaration["name"]!.GetValue<string>());
        Assert.Equal("OBJECT", declaration["parameters"]!["type"]!.GetValue<string>());
        Assert.Equal("STRING", declaration["parameters"]!["properties"]!["from"]!["type"]!.GetValue<string>());
        Assert.Equal("INTEGER", declaration["parameters"]!["properties"]!["year"]!["type"]!.GetValue<string>());
        Assert.Equal(["year"], declaration["parameters"]!["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray());

        Assert.Equal("minimal", json["generationConfig"]!["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>());
    }

    [Fact]
    public void BuildRequest_ReplaysTheModelsTurn_AndAnswersWithFunctionResponses()
    {
        using var raw = JsonDocument.Parse("""
            {"role":"model","parts":[{"functionCall":{"id":"abc","name":"get_thing","args":{"year":2026}},"thoughtSignature":"sig-1"}]}
            """);
        var call = new ModelToolCall(
            "get_thing", new Dictionary<string, JsonElement> { ["year"] = JsonSerializer.SerializeToElement(2026) }, "abc");
        var turn = new ModelTurn(null, [call], raw.RootElement.Clone());
        var step = new AssistantStep(turn, [new ModelToolResponse(call, JsonSerializer.SerializeToElement(new { total = 12.5 }))]);

        var contents = GemmaAssistantModel.BuildRequest(Conversation([step]), "minimal")["contents"]!.AsArray();

        Assert.Equal(3, contents.Count);
        Assert.Equal("model", contents[1]!["role"]!.GetValue<string>());
        Assert.Equal("sig-1", contents[1]!["parts"]![0]!["thoughtSignature"]!.GetValue<string>());

        Assert.Equal("user", contents[2]!["role"]!.GetValue<string>());
        var response = contents[2]!["parts"]![0]!["functionResponse"]!;
        Assert.Equal("get_thing", response["name"]!.GetValue<string>());
        Assert.Equal("abc", response["id"]!.GetValue<string>());
        Assert.Equal(12.5m, response["response"]!["total"]!.GetValue<decimal>());
    }

    [Fact]
    public void ParseResponse_ReadsPlainText()
    {
        var turn = GemmaAssistantModel.ParseResponse("""
            {"candidates":[{"content":{"role":"model","parts":[{"text":"Hello "},{"text":"world"}]}}]}
            """);

        Assert.Equal("Hello world", turn.Text);
        Assert.Empty(turn.ToolCalls);
    }

    [Fact]
    public void ParseResponse_ReadsFunctionCalls_AndSkipsThoughts()
    {
        var turn = GemmaAssistantModel.ParseResponse("""
            {"candidates":[{"content":{"role":"model","parts":[
              {"text":"thinking about it","thought":true},
              {"functionCall":{"id":"x1","name":"get_financial_summary","args":{"from":"2026-01-01","year":2026}}}
            ]}}]}
            """);

        Assert.Null(turn.Text);
        var call = Assert.Single(turn.ToolCalls);
        Assert.Equal("get_financial_summary", call.Name);
        Assert.Equal("x1", call.Id);
        Assert.Equal("2026-01-01", call.Arguments["from"].GetString());
        Assert.Equal(2026, call.Arguments["year"].GetInt32());
        Assert.NotNull(turn.RawContent);
    }

    [Fact]
    public void ParseResponse_HandlesMissingContent_AndRejectsUnusableAnswers()
    {
        var empty = GemmaAssistantModel.ParseResponse("""{"candidates":[{"finishReason":"MALFORMED_FUNCTION_CALL"}]}""");
        Assert.Null(empty.Text);
        Assert.Empty(empty.ToolCalls);

        Assert.Throws<AssistantUnavailableException>(() => GemmaAssistantModel.ParseResponse("""{"candidates":[]}"""));
        Assert.Throws<AssistantUnavailableException>(() => GemmaAssistantModel.ParseResponse("not json at all"));
    }

    [Fact]
    public async Task Generate_CallsTheModelEndpointWithTheKeyInAHeader()
    {
        var handler = new RecordingHandler(() => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""{"candidates":[{"content":{"parts":[{"text":"Hi"}]}}]}""")
        });

        var turn = await Client(handler).GenerateAsync(Conversation(), CancellationToken.None);

        var call = Assert.Single(handler.Calls);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemma-4-26b-a4b-it:generateContent", call.Url);
        Assert.Equal("test-key", call.Key);
        Assert.DoesNotContain("test-key", call.Url);
        Assert.Equal("Hi", turn.Text);
    }

    [Fact]
    public async Task Generate_WhenGoogleRejectsOrTheNetworkFails_ThrowsWithoutLeakingDetails()
    {
        var rejecting = new RecordingHandler(() => new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("quota exceeded for secret-project")
        });
        var failing = new RecordingHandler(() => throw new HttpRequestException("network down"));

        var first = await Assert.ThrowsAsync<AssistantUnavailableException>(
            () => Client(rejecting).GenerateAsync(Conversation(), CancellationToken.None));
        Assert.DoesNotContain("secret-project", first.Message);

        await Assert.ThrowsAsync<AssistantUnavailableException>(
            () => Client(failing).GenerateAsync(Conversation(), CancellationToken.None));
    }
}
