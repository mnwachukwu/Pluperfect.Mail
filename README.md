# Pluperfect.Mail

How every Studio TM14 service sends mail. One interface, two transports, and no SMTP anywhere.

| | |
|---|---|
| `src/Pluperfect.Mail` | The library. `net10.0`, consumed by project reference. |
| `tests/Pluperfect.Mail.Tests` | NUnit, asserting against composed messages rather than mocks. |

## The shape

```csharp
await mailer.SendAsync(new MailRequest
{
    From = new MailAddress("no-reply@pluperfect.dev", "Pluperfect Development"),
    To = new MailAddress("mattn@pluperfect.dev"),
    ReplyTo = new MailAddress(form.Email, form.Name),
    Subject = "Contact form",
    HtmlBody = html,
    TextBody = text,
});
```

`IMailer` has one method. Which transport carries the message is a deployment decision expressed in
configuration, not a choice the call site makes — which is what lets the same code path run offline
against a directory of `.eml` files and in production against Postmark.

Failure is an exception. `MailDeliveryException` is thrown by every transport, so a caller's error
handling does not have to know what is underneath. Nothing returns a status code, because a return
value invites being ignored.

## Configuration

Bound to the `Mail` section. A consuming application binds its own options to the same section for
the addresses it corresponds with; this library reads only the keys below.

| Key | |
|---|---|
| `Mail:PickupDirectory` | When set, messages are written here as `.eml` and nothing is delivered |
| `Mail:ServerToken` | The Postmark **server** token. Required unless the pickup directory is set |
| `Mail:TimeoutSeconds` | How long to wait on Postmark. Defaults to 10 |

```csharp
builder.Services.AddPluperfectMail(builder.Configuration);
```

The transport is chosen once, at registration. `PickupDirectory` wins if it is set; otherwise a
server token is required. A configuration with neither fails at startup, where systemd reports it,
rather than on the first visitor who submits a form.

### Where the token lives

In the deployment's own configuration file on its own box, mode `0600`, or in an environment
variable. Not in this repository, not in a container image, and **not in a CI secret** — a
repository secret is reachable by every workflow run and everyone who can approve one, and CI has no
reason to send mail. CI's job is to deliver files and restart a service.

A *server* token rather than an account token: it can send from one Postmark server and do nothing
else, so a leaked one costs a single project's sending rather than the account.

## Developing against it

Set `Mail:PickupDirectory` and every message lands on disk as a real `.eml`, openable in any mail
client and readable exactly as the recipient would see it. No network, no credentials, no risk of
mailing a stranger from a laptop. That is the same composition a real transport puts on the wire, so
a header that would be wrong in production is wrong in the file too.

## Why there is no SMTP

Postmark is reached over HTTPS. There is no SMTP credential to leak, no assumption that a host can
open outbound 587, and a refusal comes back as a specific error code rather than as a numeric reply
to be parsed out of a string. `MimeKit` is present only to compose the `.eml` that `FileMailer`
writes; `MailKit` is deliberately absent.

## Deliberate omissions

No attachments, no templating, no batching, no scheduling. Nothing in this fleet sends mail that
needs them, and each is easy to add later and impossible to remove once a caller depends on it.

`MessageStream` is pinned to `outbound`, Postmark's transactional stream. Postmark applies different
suppression rules to broadcast streams, and a transactional message judged by broadcast rules is
judged by rules that were never meant for it.

## Consuming it

By project reference to a sibling checkout, the same way `TM14.Networking` is consumed:

```xml
<ProjectReference Include="..\..\..\Pluperfect.Mail\src\Pluperfect.Mail\Pluperfect.Mail.csproj" />
```

In CI, check this repository out alongside:

```yaml
- uses: actions/checkout@v7
  with:
    repository: mnwachukwu/Pluperfect.Mail
    path: Pluperfect.Mail
```

Unpinned, so every consumer builds against the current library. That is what you want while several
repositories are adopting it at once; add `ref:` to a workflow when a consumer needs to stop moving.

## Building

```bash
dotnet build
```

```bash
dotnet test
```

Warnings are errors, code style is enforced in the build, and restores are locked. A `packages.lock.json`
that changes is a dependency that moved, and it is committed so that shows up in review.
