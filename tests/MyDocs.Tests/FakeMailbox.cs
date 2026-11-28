using MimeKit;
using MyDocs.Server.Infrastructure.Mail;

namespace MyDocs.Tests;

/// <summary>Postfach im Speicher statt IMAP: Eingang und Ordner, in die verschoben wurde.</summary>
public sealed class FakeMailbox : IMailboxConnector
{
    private readonly Lock _lock = new();
    private readonly List<(string Id, MimeMessage Message)> _inbox = [];
    private readonly Dictionary<string, List<MimeMessage>> _folders = new(StringComparer.Ordinal);
    private int _next;

    public ImapConfig? LastConfig { get; private set; }

    public string Add(MimeMessage message)
    {
        lock (_lock)
        {
            var id = (++_next).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _inbox.Add((id, message));
            return id;
        }
    }

    public IReadOnlyList<MimeMessage> Folder(string name)
    {
        lock (_lock)
        {
            return _folders.TryGetValue(name, out var list) ? [.. list] : [];
        }
    }

    public bool Contains(MimeMessage message)
    {
        lock (_lock)
        {
            return _inbox.Any(m => ReferenceEquals(m.Message, message));
        }
    }

    public Task<IMailboxSession> OpenAsync(ImapConfig config, CancellationToken ct)
    {
        LastConfig = config;
        return Task.FromResult<IMailboxSession>(new Session(this));
    }

    private sealed class Session(FakeMailbox box) : IMailboxSession
    {
        public Task<IReadOnlyList<MailSummary>> ListAsync(int limit, CancellationToken ct)
        {
            lock (box._lock)
            {
                return Task.FromResult<IReadOnlyList<MailSummary>>([.. box._inbox.Take(limit).Select(m => new MailSummary(m.Id, Size(m.Message)))]);
            }
        }

        public Task<MimeMessage> GetAsync(string id, CancellationToken ct)
        {
            lock (box._lock)
            {
                return Task.FromResult(box._inbox.First(m => m.Id == id).Message);
            }
        }

        public Task MoveAsync(string id, string folder, CancellationToken ct)
        {
            lock (box._lock)
            {
                var entry = box._inbox.First(m => m.Id == id);
                box._inbox.Remove(entry);
                if (!box._folders.TryGetValue(folder, out var list))
                {
                    box._folders[folder] = list = [];
                }

                list.Add(entry.Message);
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static long Size(MimeMessage message)
        {
            using var buffer = new MemoryStream();
            message.WriteTo(buffer);
            return buffer.Length;
        }
    }
}
