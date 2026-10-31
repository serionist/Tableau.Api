using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Tableau.Api.Pipeline;

namespace Tableau.Api;

/// <summary>
/// A file Tableau sends, such as a workbook, a data source or a view's export, returned as soon as its headers have come:
/// its <see cref="Content"/> streams from the server as it is read, so a file of any size takes no more memory than a
/// buffer. It holds the connection until it is disposed, so dispose it, with <c>await using</c>, once it is read or saved.
/// </summary>
/// <remarks>
/// The call's token and <see cref="TableauClientOptions.TransferTimeout"/> keep running while the content is read: the
/// token's cancellation stops a read with <see cref="OperationCanceledException"/>, and the timeout with
/// <see cref="TableauException"/> (<see cref="TableauErrorKind.Timeout"/>). A connection that breaks off mid-file fails a
/// read with <see cref="TableauException"/> (<see cref="TableauErrorKind.Http"/>); a download is never sent again once
/// its content has begun.
/// </remarks>
public sealed class TableauDownload : IDisposable, IAsyncDisposable
{
    private readonly DownloadStream _content;
    private readonly HttpResponseMessage? _response;
    private readonly CancellationTokenSource? _transfer;
    private int _saving;
    private int _disposed;

    /// <summary>
    /// A download over a stream of your own, for a test or a mock of a call that returns one. It owns
    /// <paramref name="content"/>, which it disposes when it is disposed. <see cref="FileName"/> is cleaned as a
    /// server's is, and <see cref="Kind"/> comes from the name's extension or the media type, never from the content,
    /// which a constructor can't read ahead.
    /// </summary>
    /// <param name="content">The file's content, readable.</param>
    /// <param name="fileName">The file's name, as a server would give it; null for none.</param>
    /// <param name="contentType">The media type, such as <c>application/pdf</c>; null for none.</param>
    /// <param name="length">The length in bytes, or null when not known.</param>
    /// <exception cref="ArgumentException"><paramref name="content"/> can't be read, or the length is negative.</exception>
    public TableauDownload(Stream content, string? fileName = null, string? contentType = null, long? length = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead) throw new ArgumentException("The content must be a stream that can be read.", nameof(content));
        if (length < 0) throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The length is {length}: give 0 or more, or null."), nameof(length));
        _content = new DownloadStream(content, ReadOnlyMemory<byte>.Empty, call: null);
        FileName = DownloadFiles.Clean(fileName);
        ContentType = contentType;
        Length = length;
        Kind = DownloadFiles.KindOf(FileName, contentType);
    }

    private TableauDownload(DownloadStream content, HttpResponseMessage response, CancellationTokenSource transfer, string? fileName,
        string? contentType, long? length, TableauFileKind kind)
    {
        _content = content;
        _response = response;
        _transfer = transfer;
        FileName = fileName;
        ContentType = contentType;
        Length = length;
        Kind = kind;
    }

    /// <summary>
    /// The file's content, read forward, once: a read honours its own token, the call's, and the call's
    /// <see cref="TableauClientOptions.TransferTimeout"/>.
    /// </summary>
    public Stream Content => _content;

    /// <summary>
    /// The file's name, from the answer's <c>Content-Disposition</c>, or null when it gave none. It is a name alone, safe
    /// to join to a folder of yours: anything before the last <c>/</c> or <c>\</c> is dropped, and so are the characters
    /// a file name can't hold on Windows, control and format characters, and trailing dots and spaces; a device name
    /// such as <c>CON</c> gets a leading <c>_</c>, and a name over 255 characters is cut, its extension kept.
    /// </summary>
    public string? FileName { get; }

    /// <summary>The media type the answer's <c>Content-Type</c> gives, such as <c>application/pdf</c>, or null when it gave none.</summary>
    public string? ContentType { get; }

    /// <summary>The file's length in bytes, from the answer's <c>Content-Length</c>, or null when it gave none.</summary>
    public long? Length { get; }

    /// <summary>
    /// What the file is: from <see cref="FileName"/>'s extension, such as <c>.twbx</c>; else from
    /// <see cref="ContentType"/>, such as <c>image/png</c>; else from its first bytes, a zip's, a PDF's or a PNG's
    /// signature; else <see cref="TableauFileKind.Unknown"/>.
    /// </summary>
    public TableauFileKind Kind { get; }

    /// <summary>
    /// Writes the content to a file. It is written beside <paramref name="path"/> under a temporary name, then moved
    /// there, so a download that fails leaves no part of a file behind, and no file that was there is lost.
    /// </summary>
    /// <param name="path">The file to write, such as <c>Path.Combine(folder, download.FileName)</c>; its folder must exist.</param>
    /// <param name="overwrite">Whether to replace a file already there; by default such a file is left alone and the call fails.</param>
    /// <param name="cancellationToken">Stops the save; the call's own token stops it too.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty, or names a folder.</exception>
    /// <exception cref="InvalidOperationException">The content has been read already, or saved.</exception>
    /// <exception cref="IOException">
    /// A file is at <paramref name="path"/> and <paramref name="overwrite"/> is false, or the file can't be written.
    /// </exception>
    /// <exception cref="TableauException">The download failed while it was read.</exception>
    public async Task SaveToFileAsync(string path, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var target = Path.GetFullPath(path);
        if (Path.GetFileName(target).Length == 0)
            throw new ArgumentException($"The path {target} names a folder: give the file's path, such as Path.Combine(folder, download.FileName).", nameof(path));
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        // Refused before anything is read, so the same download can still be saved elsewhere, or with overwrite.
        if (!overwrite && File.Exists(target))
            throw new IOException($"The file {target} exists already: pass overwrite: true to replace it.");
        if (_content.Started || Interlocked.Exchange(ref _saving, 1) != 0)
            throw new InvalidOperationException("The download's content has been read already: a download can be read, or saved, once.");
        // A full path with a file name has a folder.
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".tableau-{Path.GetRandomFileName()}.partial");
        try
        {
            var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await using (file.ConfigureAwait(false))
                await _content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, overwrite);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The temporary file stays, under its hidden name; the failure that matters is the one thrown below.
            }
            throw;
        }
    }

    /// <summary>Closes the content and the connection under it. Disposing twice does nothing more.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _content.Dispose();
        _response?.Dispose();
        _transfer?.Dispose();
    }

    /// <summary>Closes the content and the connection under it, as <see cref="Dispose"/> does.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _content.DisposeAsync().ConfigureAwait(false);
        _response?.Dispose();
        _transfer?.Dispose();
    }

    /// <summary>
    /// A download over an answer whose headers have come: its name, media type and length from them, and its kind from
    /// them or, when they don't say, from its first bytes, which are read ahead and given back first.
    /// </summary>
    internal static async Task<TableauDownload> OpenAsync(HttpResponseMessage response, CancellationTokenSource transfer,
        DownloadCall call)
    {
        var headers = response.Content.Headers;
        var fileName = DownloadFiles.FileNameOf(headers);
        var contentType = headers.ContentType?.MediaType;
        var kind = DownloadFiles.KindOf(fileName, contentType);
        var stream = await response.Content.ReadAsStreamAsync(transfer.Token).ConfigureAwait(false);
        var start = Array.Empty<byte>();
        try
        {
            if (kind == TableauFileKind.Unknown)
            {
                start = new byte[DownloadFiles.SignatureLength];
                var length = 0;
                int read;
                while (length < start.Length
                       && (read = await stream.ReadAsync(start.AsMemory(length), transfer.Token).ConfigureAwait(false)) > 0)
                    length += read;
                start = start[..length];
                kind = DownloadFiles.KindOf(start);
            }
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return new TableauDownload(new DownloadStream(stream, start, call), response, transfer, fileName, contentType,
            headers.ContentLength, kind);
    }
}
