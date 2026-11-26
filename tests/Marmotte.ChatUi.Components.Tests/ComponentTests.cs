using Bunit;
using Marmotte.ChatUi.Components;
using Marmotte.ChatUi.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Marmotte.ChatUi.Components.Tests;

/// <summary>Exercises the rendered UI contract, input events, awaited callbacks, safe content, and lifecycle accessibility.</summary>
public sealed class ComponentTests
{
    [Fact]
    public void Model_text_and_attachment_names_are_encoded_and_partial_content_survives_failure()
    {
        using var context = CreateContext();
        var message = Message("<script>alert('model')</script>\n<script>second</script>", GenerationStatus.Failed) with
        {
            Attachments = [Attachment(AttachmentStatus.Failed) with { FileName = "<img src=x onerror=alert(1)>" }]
        };
        var cut = context.Render<ChatMessage>(parameters => parameters.Add(component => component.Message, message));

        Assert.Empty(cut.FindAll("script, img"));
        Assert.Equal(message.Content, cut.Find(".mcui-message-content").TextContent);
        Assert.Contains("partial response", cut.Find(".mcui-message-note").TextContent, StringComparison.Ordinal);
        Assert.Contains(message.Attachments[0].FileName, cut.Find(".mcui-attachment").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GenerationStatus.Pending, "Waiting")]
    [InlineData(GenerationStatus.Streaming, "progress")]
    [InlineData(GenerationStatus.Completed, "complete")]
    [InlineData(GenerationStatus.Cancelled, "Partial response retained")]
    [InlineData(GenerationStatus.Failed, "partial response is retained")]
    public void Lifecycle_has_one_polite_atomic_announcement(GenerationStatus state, string expected)
    {
        using var context = CreateContext();
        var cut = context.Render<GenerationIndicator>(parameters => parameters.Add(component => component.Status, state));
        var status = cut.Find("[role=status]");

        Assert.Equal("polite", status.GetAttribute("aria-live"));
        Assert.Equal("true", status.GetAttribute("aria-atomic"));
        Assert.Contains(expected, status.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_history_can_be_replaced_and_message_templates_keep_speaker_context()
    {
        using var context = CreateContext();
        var empty = context.Render<MessageList>(parameters => parameters.Add(component => component.EmptyContent, "A host supplied welcome"));
        Assert.Contains("A host supplied welcome", empty.Markup, StringComparison.Ordinal);
        Assert.Equal("0", empty.Find("[role=region]").GetAttribute("tabindex"));

        var cut = context.Render<MessageList>(parameters => parameters
            .Add(component => component.Messages, new[] { Message("original", GenerationStatus.Completed) })
            .Add(component => component.MessageTemplate, message => builder => builder.AddContent(0, $"Custom: {message.Content}")));
        Assert.Contains("Custom: original", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("Assistant", cut.Find("article").GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll("[aria-live]"));
    }

    [Fact]
    public async Task Composer_rejects_blank_input_and_awaits_acceptance_before_clearing()
    {
        using var context = CreateContext();
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var draft = "  ";
        var cut = context.Render<MessageComposer>(parameters => parameters
            .Add(component => component.Draft, draft)
            .Add(component => component.DraftChanged, value => draft = value)
            .Add(component => component.OnSend, async _ => { calls++; await accepted.Task; }));

        await cut.Find("form").SubmitAsync();
        Assert.Equal(0, calls);
        cut.Render(parameters => parameters.Add(component => component.Draft, "Keep my draft"));
        var submission = cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.Equal(1, calls));
        Assert.True(cut.Find("button").HasAttribute("disabled"));
        await cut.Find("form").SubmitAsync();
        Assert.Equal(1, calls);
        Assert.NotEqual(string.Empty, draft);

        accepted.SetResult();
        await submission;
        Assert.Equal(string.Empty, draft);
        Assert.Single(context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
    }

    [Fact]
    public async Task Failed_submission_retains_draft_and_hides_exception_details()
    {
        using var context = CreateContext();
        var draft = "Keep me";
        var cut = context.Render<MessageComposer>(parameters => parameters
            .Add(component => component.Draft, draft)
            .Add(component => component.DraftChanged, value => draft = value)
            .Add(component => component.OnSend, _ => Task.FromException(new InvalidOperationException("secret-provider-data"))));

        await cut.Find("form").SubmitAsync();
        Assert.Equal("Keep me", draft);
        Assert.Contains("draft is retained", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-provider-data", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelled_submission_retains_draft_and_edit_clears_feedback()
    {
        using var context = CreateContext();
        var draft = "Keep me";
        var cut = context.Render<MessageComposer>(parameters => parameters
            .Add(component => component.Draft, draft)
            .Add(component => component.DraftChanged, value => draft = value)
            .Add(component => component.OnSend, _ => Task.FromException(new OperationCanceledException())));

        await cut.Find("form").SubmitAsync();
        Assert.Equal("Keep me", draft);
        Assert.Contains("cancelled", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        cut.Find("textarea").Input("Edited draft");
        Assert.Equal("Edited draft", draft);
        Assert.Empty(cut.Find("[role=alert]").TextContent);
    }

    [Theory]
    [InlineData(AttachmentStatus.Uploaded)]
    [InlineData(AttachmentStatus.Preparing)]
    [InlineData(AttachmentStatus.Ready)]
    [InlineData(AttachmentStatus.Failed)]
    public async Task Pending_attachments_never_disappear_into_a_text_submission(AttachmentStatus state)
    {
        using var context = CreateContext();
        var calls = 0;
        Guid? removed = null;
        var attachment = Attachment(state);
        var cut = context.Render<ChatWidget>(parameters => parameters
            .Add(component => component.Attachments, new[] { attachment })
            .Add(component => component.Draft, "A draft")
            .Add(component => component.DraftChanged, _ => { })
            .Add(component => component.OnSend, _ => calls++)
            .Add(component => component.OnRemoveAttachment, id => removed = id));

        Assert.True(cut.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.Contains("cannot send attachments", cut.Markup, StringComparison.Ordinal);
        await cut.Find("form").SubmitAsync();
        Assert.Equal(0, calls);
        cut.Find("button[aria-label]").Click();
        Assert.Equal(attachment.Id, removed);
        Assert.Empty(cut.FindAll("img, a"));
    }

    [Fact]
    public async Task Cancellation_waits_for_host_acknowledgement_and_blocks_duplicate_requests()
    {
        using var context = CreateContext();
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cut = context.Render<ChatWidget>(parameters => parameters
            .Add(component => component.Status, GenerationStatus.Streaming)
            .Add(component => component.OnCancel, async () => { calls++; await acknowledged.Task; }));

        var cancellation = cut.Find(".mcui-generation button").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(1, calls));
        Assert.Contains("Stopping", cut.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.True(cut.Find(".mcui-generation button").HasAttribute("disabled"));
        await cut.Find(".mcui-generation button").ClickAsync(new MouseEventArgs());
        Assert.Equal(1, calls);
        acknowledged.SetResult();
        await cancellation;
        cut.Render(parameters => parameters.Add(component => component.Status, GenerationStatus.Cancelled));
        Assert.Contains("stopped", cut.Find("[role=status]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_selection_and_branding_are_host_controlled_per_instance()
    {
        using var context = CreateContext();
        var selected = ThemePreference.System;
        var first = context.Render<ChatWidget>(parameters => parameters
            .Add(component => component.ThemeChanged, theme => selected = theme)
            .Add(component => component.Branding, "Host brand")
            .Add(component => component.Style, "--mcui-accent: purple"));
        var second = context.Render<ChatWidget>(parameters => parameters.Add(component => component.Theme, ThemePreference.Light));

        first.Find("select").Change("Dark");
        Assert.Equal(ThemePreference.Dark, selected);
        first.Render(parameters => parameters.Add(component => component.Theme, selected));
        Assert.Equal("dark", first.Find(".mcui-theme").GetAttribute("data-theme"));
        Assert.Equal("--mcui-accent: purple", first.Find(".mcui-theme").GetAttribute("style"));
        Assert.Equal("light", second.Find(".mcui-theme").GetAttribute("data-theme"));
        Assert.Contains("Host brand", first.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Host brand", second.Markup, StringComparison.Ordinal);
        Assert.Empty(second.FindAll("img"));
        first.Find("select").Change("99");
        Assert.Equal(ThemePreference.Dark, selected);
    }

    [Fact]
    public void Widget_passes_the_actual_bound_draft_to_its_composer()
    {
        using var context = CreateContext();
        var cut = context.Render<ChatWidget>(parameters => parameters.Add(component => component.Draft, "Host draft"));
        Assert.Equal("Host draft", cut.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public void Static_rendering_disables_actions_and_multiple_composers_have_unique_labels()
    {
        using var context = CreateContext(interactive: false);
        var first = context.Render<ChatWidget>(parameters => parameters
            .Add(component => component.Draft, "Hello")
            .Add(component => component.DraftChanged, _ => { })
            .Add(component => component.OnSend, _ => { }));
        var second = context.Render<MessageComposer>();

        Assert.All(first.FindAll("button, select, textarea"), element => Assert.True(element.HasAttribute("disabled")));
        Assert.Equal(first.Find("textarea").Id, first.Find("label[for]").GetAttribute("for"));
        Assert.NotEqual(first.Find("textarea").Id, second.Find("textarea").Id);
    }

    [Theory]
    [InlineData(GenerationStatus.Pending)]
    [InlineData(GenerationStatus.Streaming)]
    public async Task Active_generation_blocks_new_submissions(GenerationStatus status)
    {
        using var context = CreateContext();
        var calls = 0;
        var cut = context.Render<ChatWidget>(parameters => parameters
            .Add(component => component.Status, status)
            .Add(component => component.Draft, "Cannot send yet")
            .Add(component => component.DraftChanged, _ => { })
            .Add(component => component.OnSend, _ => calls++));

        await cut.Find("form").SubmitAsync();
        Assert.Equal(0, calls);
        Assert.True(cut.Find("textarea").HasAttribute("readonly"));
    }

    /// <summary>Creates an isolated renderer with explicit interactivity and a simulated browser focus API; the caller disposes it.</summary>
    private static BunitContext CreateContext(bool interactive = true)
    {
        var context = new BunitContext();
        context.SetRendererInfo(new RendererInfo(interactive ? "Server" : "Static", interactive));
        context.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true).SetVoidResult();
        return context;
    }

    /// <summary>Creates deterministic metadata for lifecycle tests, without storage or content URLs.</summary>
    private static AttachmentView Attachment(AttachmentStatus state) => new(Guid.NewGuid(), "notes.png", "image/png", 1234, state, 10, 10);

    /// <summary>Creates an assistant snapshot with arbitrary untrusted text for rendering assertions.</summary>
    private static MessageView Message(string text, GenerationStatus state) => new(Guid.NewGuid(), Guid.NewGuid(), 1,
        ChatRole.Assistant, text, state, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, [], null);
}
