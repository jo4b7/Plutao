#!/usr/bin/env python3
# Plutao v0.5.6 updater
# Base obrigatoria: v0.5.5
# Commit canonico: 41abdc12870ad70dafe04a5e8e1de82de963c7e9

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import zipfile
from datetime import datetime
from pathlib import Path

BASE_SHA = "41abdc12870ad70dafe04a5e8e1de82de963c7e9"
VERSION = "0.5.6"


def run(cmd: list[str], cwd: Path, check: bool = True) -> subprocess.CompletedProcess:
    result = subprocess.run(cmd, cwd=cwd, text=True, capture_output=True, shell=False)
    if check and result.returncode != 0:
        raise RuntimeError(
            f"Comando falhou ({result.returncode}): {' '.join(map(str, cmd))}\n"
            f"{result.stdout}\n{result.stderr}"
        )
    return result


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(
            f"{label}: esperado exatamente 1 trecho na v0.5.5, encontrado {count}. "
            "Nenhum arquivo sera gravado."
        )
    return text.replace(old, new, 1)


def zip_tree(root: Path, out: Path, exclude_build: bool = True) -> None:
    if out.exists():
        out.unlink()

    excluded_dirs = {".git", ".vs"}
    if exclude_build:
        excluded_dirs |= {"bin", "obj", "release", "publish"}

    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as archive:
        for item in root.rglob("*"):
            rel = item.relative_to(root)
            if any(part in excluded_dirs for part in rel.parts):
                continue
            if item.is_file():
                archive.write(item, rel.as_posix())


def patch_appcache(text: str) -> str:
    old = '''        if (host.Contains("youtube.com"))
        {
            if (path.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                var query = ParseQuery(uri.Query);
                if (query.TryGetValue("v", out var id) && !string.IsNullOrWhiteSpace(id))
                    return $"https://www.youtube.com/watch?v={id}";
            }

            foreach (var prefix in new[] { "/shorts/", "/live/" })
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var id = path[prefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(id))
                        return $"https://www.youtube.com/watch?v={id}";
                }
            }
        }
'''
    new = '''        if (host.Contains("youtube.com"))
        {
            if (path.Equals("/watch", StringComparison.OrdinalIgnoreCase))
            {
                var query = ParseQuery(uri.Query);
                if (query.TryGetValue("v", out var id) && !string.IsNullOrWhiteSpace(id))
                    return $"https://www.youtube.com/watch?v={id}";
            }

            if (path.Equals("/playlist", StringComparison.OrdinalIgnoreCase))
            {
                var playlistQuery = ParseQuery(uri.Query);
                if (playlistQuery.TryGetValue("list", out var listId) && !string.IsNullOrWhiteSpace(listId))
                    return $"https://www.youtube.com/playlist?list={Uri.EscapeDataString(listId)}";
            }

            foreach (var prefix in new[] { "/shorts/", "/live/" })
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var id = path[prefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(id))
                        return $"https://www.youtube.com/watch?v={id}";
                }
            }
        }
'''
    text = replace_once(text, old, new, "AppCache.NormalizeUrl")
    text = replace_once(
        text,
        '"collection-v054-instagram-meta1-tiktok-gallery1-youtube-tabs1|',
        '"collection-v056-instagram-categories2-tiktok-gallery1-youtube-tabs2|',
        "AppCache collection key",
    )
    text = replace_once(
        text,
        '"download-cache-v052-finalpath1",',
        '"download-cache-v056-finalpath2",',
        "AppCache download key",
    )
    return text


def patch_progress(text: str) -> str:
    old = '''    public double OverallPercent
    {
        get
        {
            if (CollectionIndex > 0 && CollectionCount > 0)
                return Math.Clamp(((CollectionIndex - 1) + (Percent / 100.0)) / CollectionCount * 100.0, 0, 100);

            return ItemCount <= 0
                ? Percent
                : Math.Clamp(((ItemIndex - 1) + (Percent / 100.0)) / ItemCount * 100.0, 0, 100);
        }
    }
'''
    new = '''    public double OverallPercent
    {
        get
        {
            var innerFraction = Math.Clamp(Percent / 100.0, 0, 1);

            if (CollectionIndex > 0 && CollectionCount > 0)
                innerFraction = Math.Clamp(((CollectionIndex - 1) + innerFraction) / CollectionCount, 0, 1);

            if (ItemIndex > 0 && ItemCount > 0)
                return Math.Clamp(((ItemIndex - 1) + innerFraction) / ItemCount * 100.0, 0, 100);

            return Math.Clamp(innerFraction * 100.0, 0, 100);
        }
    }
'''
    return replace_once(text, old, new, "DownloadProgressInfo.OverallPercent")


def patch_mainform(text: str) -> str:
    text = replace_once(
        text,
        '    private readonly CheckBox chkCompatibleMp4 = new() { Text = "Compatibilidade automática (H.264 até 1080p • HEVC acima)", Checked = true, AutoSize = true };\n',
        '',
        "MainForm remove dead compatibility checkbox",
    )
    text = replace_once(
        text,
        'private readonly Button btnUpdate = new() { Text = "Atualizar componentes" };',
        'private readonly Button btnUpdate = new() { Text = "Atualizar componentes", AutoSize = true, MinimumSize = new Size(180, 34) };',
        "MainForm update button geometry",
    )
    text = replace_once(
        text,
        'private readonly Button btnToggleLog = new() { Text = "Diagnóstico", AutoSize = true };',
        'private readonly Button btnToggleLog = new() { Text = "Diagnóstico", AutoSize = true };\n'
        '    private readonly Button btnToggleResults = new() { Text = "Mostrar resultados", AutoSize = true, Visible = false };',
        "MainForm results toggle",
    )
    text = replace_once(
        text,
        'Text = "Execução em andamento • alterações feitas aqui serão usadas na próxima tarefa.",',
        'Text = "Execução em andamento • a tarefa atual mantém as opções do início; alterações valem para a próxima.",',
        "MainForm busy hint",
    )
    text = replace_once(
        text,
        'private readonly Label lblDestination = new() { Text = "Destino não definido", AutoSize = true, AutoEllipsis = true };',
        'private readonly Label lblDestination = new() { Text = "Destino não definido", AutoSize = false, AutoEllipsis = true };',
        "MainForm destination label",
    )
    text = replace_once(
        text,
        'private readonly List<string> _selectedCollectionUrls = new();',
        'private readonly List<string> _selectedCollectionUrls = new();\n    private string? _activeOutputDirectory;',
        "MainForm active output",
    )
    text = replace_once(
        text,
        'Text = "Baixe vídeos, áudios, perfis e playlists  •  v0.5.5",',
        'Text = "Baixe vídeos, áudios, perfis e playlists  •  v0.5.6",',
        "MainForm display version",
    )
    text = replace_once(
        text,
        '''        headerActions.Controls.Add(btnOpenDownloads);
        headerActions.Controls.Add(btnToggleLog);
        headerActions.Controls.Add(btnAdvanced);
''',
        '''        headerActions.Controls.Add(btnOpenDownloads);
        headerActions.Controls.Add(btnToggleResults);
        headerActions.Controls.Add(btnToggleLog);
        headerActions.Controls.Add(btnAdvanced);
''',
        "MainForm header actions",
    )
    text = replace_once(
        text,
        '''        var width = Math.Clamp((int)(ClientSize.Width * 0.38), 390, 520);
        width = Math.Min(width, Math.Max(320, ClientSize.Width - 40));
''',
        '''        var width = Math.Clamp((int)(ClientSize.Width * 0.46), 460, 660);
        width = Math.Min(width, Math.Max(360, ClientSize.Width - 32));
''',
        "MainForm settings drawer size",
    )
    text = replace_once(
        text,
        'SetNamedHeight("previewCard", compact ? 112 : roomy ? 126 : 120);',
        'SetNamedHeight("previewCard", compact ? 124 : roomy ? 132 : 126);',
        "MainForm preview height",
    )
    text = replace_once(
        text,
        '        chkCompatibleMp4.Checked = true; // regra automática interna para MP4\n',
        '',
        "MainForm remove checkbox init",
    )
    text = replace_once(
        text,
        '''        profileSelection.HideRequested += (_, _) =>
        {
            profileSelection.Visible = false;
            UpdateWorkspaceLayout();
        };
''',
        '''        profileSelection.HideRequested += (_, _) =>
        {
            profileSelection.Visible = false;
            btnToggleResults.Visible = profileSelection.HasData;
            btnToggleResults.Text = "Mostrar resultados";
            UpdateWorkspaceLayout();
        };
        btnToggleResults.Click += (_, _) =>
        {
            if (!profileSelection.HasData)
                return;

            profileSelection.Visible = !profileSelection.Visible;
            btnToggleResults.Text = profileSelection.Visible ? "Ocultar resultados" : "Mostrar resultados";
            UpdateWorkspaceLayout();
        };
''',
        "MainForm hide/show results",
    )
    text = replace_once(
        text,
        '''        btnOpenDownloads.Click += (_, _) =>
        {
            var path = rbVideo.Checked ? txtVideoOutput.Text : txtAudioOutput.Text;
            if (string.IsNullOrWhiteSpace(path))
            {
                btnChangeDestination.PerformClick();
                return;
            }
            OpenFolder(path);
        };
''',
        '''        btnOpenDownloads.Click += (_, _) =>
        {
            var path = _busy && !string.IsNullOrWhiteSpace(_activeOutputDirectory)
                ? _activeOutputDirectory
                : rbVideo.Checked ? txtVideoOutput.Text : txtAudioOutput.Text;

            if (string.IsNullOrWhiteSpace(path))
            {
                if (!_busy)
                    btnChangeDestination.PerformClick();
                return;
            }

            OpenFolder(path);
        };
''',
        "MainForm active download folder",
    )
    text = replace_once(
        text,
        '''        _selectedCollectionUrls.Clear();
        profileSelection.ClearData();
        UpdateWorkspaceLayout();
''',
        '''        _selectedCollectionUrls.Clear();
        profileSelection.ClearData();
        btnToggleResults.Visible = false;
        btnToggleResults.Text = "Mostrar resultados";
        UpdateWorkspaceLayout();
''',
        "MainForm reset results",
    )
    text = replace_once(
        text,
        '''        await StopPreviewBeforeRunnerUseAsync();
        _tools.TemporaryDirectory = txtTemp.Text.Trim();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        btnAnalyzeCollection.Visible = false;
''',
        '''        var analysisCookies = cmbCookies.SelectedItem?.ToString() ?? "Nenhum";
        var analysisLimit = SelectedCollectionLimit();

        await StopPreviewBeforeRunnerUseAsync();
        _tools.TemporaryDirectory = txtTemp.Text.Trim();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        btnAnalyzeCollection.Visible = false;
''',
        "MainForm analysis snapshot",
    )
    text = replace_once(
        text,
        '''                targetUrl,
                cmbCookies.SelectedItem?.ToString() ?? "Nenhum",
                SelectedCollectionLimit(),
''',
        '''                targetUrl,
                analysisCookies,
                analysisLimit,
''',
        "MainForm analysis options",
    )
    text = replace_once(
        text,
        '''            if (items.Count == 0)
            {
                lblCollectionInfo.Text = "0 vídeos encontrados";
                btnAnalyzeCollection.Text = "TENTAR NOVAMENTE";
                btnAnalyzeCollection.Visible = true;
                if (!automatic)
                    MessageBox.Show(this, "Nenhum vídeo foi encontrado. Confira o diagnóstico; alguns perfis exigem Cookies do navegador.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
''',
        '''            if (items.Count == 0)
            {
                lblCollectionInfo.Text = "0 vídeos encontrados";
                lblStatus.Text = "Nenhum vídeo encontrado";
                lblStatus.ForeColor = Color.FromArgb(255, 184, 77);
                lblCurrent.Text = "A plataforma não devolveu mídias para esse link.";
                btnAnalyzeCollection.Text = "TENTAR NOVAMENTE";
                btnAnalyzeCollection.Visible = true;
                if (!automatic)
                    MessageBox.Show(this, "Nenhum vídeo foi encontrado. Confira o diagnóstico; alguns perfis exigem Cookies do navegador.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
''',
        "MainForm zero items",
    )
    text = replace_once(
        text,
        '''        if (!chkPlaylist.Checked)
            chkPlaylist.Checked = true;
''',
        '''        if (!chkPlaylist.Checked)
        {
            if (automatic)
                return;
            chkPlaylist.Checked = true;
        }
''',
        "MainForm collection mode guard",
    )
    text = replace_once(
        text,
        '''            // Se o link mudou durante a análise, ignora o resultado antigo.
            var currentUrl = CurrentUrls().Count == 1 ? CurrentUrls()[0] : string.Empty;
            if (!string.Equals(AppCache.NormalizeUrl(targetUrl), AppCache.NormalizeUrl(currentUrl), StringComparison.OrdinalIgnoreCase))
                return;
''',
        '''            // Se link, cookies, limite ou modo mudaram durante a análise,
            // ignora o resultado antigo. O finally agenda nova análise quando aplicável.
            var currentUrls = CurrentUrls();
            var currentUrl = currentUrls.Count == 1 ? currentUrls[0] : string.Empty;
            var currentCookies = cmbCookies.SelectedItem?.ToString() ?? "Nenhum";
            var currentLimit = SelectedCollectionLimit();
            if (!chkPlaylist.Checked ||
                !string.Equals(AppCache.NormalizeUrl(targetUrl), AppCache.NormalizeUrl(currentUrl), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(analysisCookies, currentCookies, StringComparison.OrdinalIgnoreCase) ||
                analysisLimit != currentLimit)
                return;
''',
        "MainForm stale collection result",
    )
    text = replace_once(
        text,
        '''        if (YtDlpRunner.IsLikelySingleMediaUrl(urls[0]) || IsLikelyCollectionUrl(urls[0]))
            _previewTimer.Start();
''',
        '''        if (YtDlpRunner.IsLikelySingleMediaUrl(urls[0]))
            _previewTimer.Start();
        else if (chkPlaylist.Checked && IsLikelyCollectionUrl(urls[0]))
            _previewTimer.Start();
''',
        "MainForm restart collection guard",
    )
    text = replace_once(
        text,
        '''            else if (IsLikelyCollectionUrl(urls[0]))
            {
                try { await AnalyzeCollectionAsync(automatic: true); } catch { }
            }
''',
        '''            else if (chkPlaylist.Checked && IsLikelyCollectionUrl(urls[0]))
            {
                try { await AnalyzeCollectionAsync(automatic: true); } catch { }
            }
''',
        "MainForm timer collection guard",
    )

    text = replace_once(
        text,
        '''            _analyzedCollectionUrl = targetUrl;
            profileSelection.LoadData(items, _runner.LastAnalyzedProfile);
            UpdateWorkspaceLayout();
''',
        '''            _analyzedCollectionUrl = targetUrl;
            profileSelection.LoadData(items, _runner.LastAnalyzedProfile);
            profileSelection.Visible = true;
            btnToggleResults.Visible = true;
            btnToggleResults.Text = "Ocultar resultados";
            UpdateWorkspaceLayout();
''',
        "MainForm show loaded results",
    )
    text = replace_once(
        text,
        '''        catch (OperationCanceledException)
        {
            lblStatus.Text = "Cancelado";
            lblStatus.ForeColor = Color.FromArgb(255, 184, 77);
            lblCurrent.Text = "Análise cancelada.";
            AppendLog("[Análise] Cancelada.");
        }
''',
        '''        catch (OperationCanceledException)
        {
            lblStatus.Text = "Cancelado";
            lblStatus.ForeColor = Color.FromArgb(255, 184, 77);
            lblCurrent.Text = "Análise cancelada.";
            lblCollectionInfo.Text = "Análise cancelada";
            btnAnalyzeCollection.Text = "TENTAR NOVAMENTE";
            var current = CurrentUrls();
            btnAnalyzeCollection.Visible = current.Count == 1 && IsLikelyCollectionUrl(current[0]);
            AppendLog("[Análise] Cancelada.");
        }
''',
        "MainForm cancelled analysis",
    )
    text = replace_once(
        text,
        '''        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task StartDownloadAsync()
''',
        '''        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
            RestartPreviewAnalysis();
        }
    }

    private async Task StartDownloadAsync()
''',
        "MainForm restart analysis",
    )
    text = replace_once(
        text,
        '''        await StopPreviewBeforeRunnerUseAsync();
        _tools.TemporaryDirectory = options.TemporaryDirectory;
        _cts = new CancellationTokenSource();
''',
        '''        _activeOutputDirectory = options.OutputDirectory;
        await StopPreviewBeforeRunnerUseAsync();
        _tools.TemporaryDirectory = options.TemporaryDirectory;
        _cts = new CancellationTokenSource();
''',
        "MainForm active output assignment",
    )
    text = replace_once(
        text,
        '''        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private int SelectedCollectionLimit()
''',
        '''        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
            _activeOutputDirectory = null;
        }
    }

    private int SelectedCollectionLimit()
''',
        "MainForm clear active output",
    )


    text = text.replace("item(ns)", "itens")
    text = text.replace("mídia(s)", "mídias")
    text = text.replace("vídeo(s)", "vídeos")
    return text


def patch_profile_panel(text: str) -> str:
    text = replace_once(
        text,
        'private static readonly ConcurrentDictionary<string, byte[]> RenderedMemoryCache = new(StringComparer.Ordinal);',
        'private static readonly ConcurrentDictionary<string, byte[]> RenderedMemoryCache = new(StringComparer.Ordinal);\n'
        '    private const int MaxMemoryCacheEntries = 512;',
        "ProfileSelectionPanel memory limit",
    )

    old_profile = '''        _profileName.Text = string.IsNullOrWhiteSpace(_profile.DisplayName)
            ? (!string.IsNullOrWhiteSpace(_profile.Username) ? _profile.Username : _profile.Platform)
            : _profile.DisplayName;
        _profileHandle.Text = string.IsNullOrWhiteSpace(_profile.Username)
            ? _profile.Platform
            : $"@{_profile.Username}  •  {_profile.Platform}";

        var stats = new List<string>();
        if (_profile.Followers.HasValue && _profile.Followers.Value > 0) stats.Add($"{FormatCount(_profile.Followers.Value)} seguidores");
        if (_profile.Following.HasValue && _profile.Following.Value > 0) stats.Add($"{FormatCount(_profile.Following.Value)} seguindo");
        if (_profile.Posts.HasValue && _profile.Posts.Value > 0) stats.Add($"{FormatCount(_profile.Posts.Value)} publicações");
'''
    new_profile = '''        var cleanUsername = (_profile.Username ?? string.Empty).Trim().TrimStart('@');
        _profileName.Text = string.IsNullOrWhiteSpace(_profile.DisplayName)
            ? (!string.IsNullOrWhiteSpace(cleanUsername) ? cleanUsername : _profile.Platform)
            : _profile.DisplayName;
        _profileHandle.Text = string.IsNullOrWhiteSpace(cleanUsername)
            ? _profile.Platform
            : $"@{cleanUsername}  •  {_profile.Platform}";

        var stats = new List<string>();
        if (_profile.Followers.HasValue) stats.Add($"{FormatCount(_profile.Followers.Value)} seguidores");
        if (_profile.Following.HasValue) stats.Add($"{FormatCount(_profile.Following.Value)} seguindo");
        if (_profile.Posts.HasValue) stats.Add($"{FormatCount(_profile.Posts.Value)} publicações");
'''
    text = replace_once(text, old_profile, new_profile, "ProfileSelectionPanel profile header")

    text = replace_once(
        text,
        '''            "Vídeos" => 0,
            "Shorts" => 1,
            "Lives" => 2,
            _ => 10
''',
        '''            "Vídeos" => 0,
            "Shorts" => 1,
            "Lives" => 2,
            "Reels" => 3,
            "Posts" => 4,
            _ => 10
''',
        "ProfileSelectionPanel category order",
    )

    text = replace_once(
        text,
        '''                HeaderText = "Selecionado",
                Width = 92,
                MinimumWidth = 82,
''',
        '''                HeaderText = "Selecionado",
                Width = 78,
                MinimumWidth = 68,
''',
        "ProfileSelectionPanel selected column",
    )
    text = replace_once(
        text,
        '''                HeaderText = "Miniatura",
                Width = 118,
                MinimumWidth = 96,
''',
        '''                HeaderText = "Miniatura",
                Width = 100,
                MinimumWidth = 82,
''',
        "ProfileSelectionPanel thumbnail column",
    )
    text = replace_once(text, 'MinimumWidth = 220,', 'MinimumWidth = 180,', "ProfileSelectionPanel video width")
    text = replace_once(text, 'MinimumWidth = 210,', 'MinimumWidth = 160,', "ProfileSelectionPanel details width")

    old_load = '''            var item = _items[rowIndex];
            if (string.IsNullOrWhiteSpace(item.ThumbnailUrl))
            {
                _thumbnailLoaded.TryAdd(rowIndex, 0);
                return;
            }

            await _thumbnailGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var image = await DownloadAndResizeAsync(item.ThumbnailUrl, 112, 70, ct).ConfigureAwait(false);
                if (image is null || ct.IsCancellationRequested) return;
'''
    new_load = '''            var item = _items[rowIndex];
            var thumbnailUrl = item.ThumbnailUrl;
            if (string.IsNullOrWhiteSpace(thumbnailUrl) &&
                !string.IsNullOrWhiteSpace(item.Id) &&
                (item.Url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                 item.Url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)))
            {
                thumbnailUrl = $"https://i.ytimg.com/vi/{item.Id}/hqdefault.jpg";
            }

            if (string.IsNullOrWhiteSpace(thumbnailUrl))
            {
                _thumbnailLoaded.TryAdd(rowIndex, 0);
                return;
            }

            await _thumbnailGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var image = await DownloadAndResizeAsync(thumbnailUrl, 112, 70, ct).ConfigureAwait(false);
                if (image is null || ct.IsCancellationRequested)
                {
                    _thumbnailLoaded.TryAdd(rowIndex, 0);
                    return;
                }
'''
    text = replace_once(text, old_load, new_load, "ProfileSelectionPanel thumbnail fallback")

    text = replace_once(
        text,
        '''                    var cached = await File.ReadAllBytesAsync(renderedPath, ct).ConfigureAwait(false);
                    RenderedMemoryCache.TryAdd(renderedKey, cached);
                    return BitmapFromBytes(cached);
''',
        '''                    var cached = await File.ReadAllBytesAsync(renderedPath, ct).ConfigureAwait(false);
                    var cachedImage = BitmapFromBytes(cached);
                    if (cachedImage is not null)
                    {
                        AddMemoryEntry(RenderedMemoryCache, renderedKey, cached);
                        return cachedImage;
                    }

                    try { File.Delete(renderedPath); } catch { }
                    RenderedMemoryCache.TryRemove(renderedKey, out _);
''',
        "ProfileSelectionPanel rendered cache recovery",
    )
    text = replace_once(
        text,
        'RenderedMemoryCache.TryAdd(renderedKey, renderedBytes);',
        'AddMemoryEntry(RenderedMemoryCache, renderedKey, renderedBytes);',
        "ProfileSelectionPanel rendered cache cap",
    )

    old_raw = '''        if (MemoryCache.TryGetValue(url, out var memory))
            return memory;

        var cachePath = CachePath(url);
        try
        {
            if (File.Exists(cachePath))
            {
                try { File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow); } catch { }
                var cached = await File.ReadAllBytesAsync(cachePath, ct).ConfigureAwait(false);
                MemoryCache.TryAdd(url, cached);
                return cached;
            }
        }
        catch { }
'''
    new_raw = '''        if (MemoryCache.TryGetValue(url, out var memory))
        {
            if (IsValidImageBytes(memory))
                return memory;
            MemoryCache.TryRemove(url, out _);
        }

        var cachePath = CachePath(url);
        try
        {
            if (File.Exists(cachePath))
            {
                try { File.SetLastAccessTimeUtc(cachePath, DateTime.UtcNow); } catch { }
                var cached = await File.ReadAllBytesAsync(cachePath, ct).ConfigureAwait(false);
                if (IsValidImageBytes(cached))
                {
                    AddMemoryEntry(MemoryCache, url, cached);
                    return cached;
                }

                try { File.Delete(cachePath); } catch { }
            }
        }
        catch { }
'''
    text = replace_once(text, old_raw, new_raw, "ProfileSelectionPanel raw cache recovery")
    text = replace_once(
        text,
        '''            if (bytes.Length == 0) return null;

            MemoryCache.TryAdd(url, bytes);
''',
        '''            if (bytes.Length == 0 || !IsValidImageBytes(bytes)) return null;

            AddMemoryEntry(MemoryCache, url, bytes);
''',
        "ProfileSelectionPanel download validation",
    )

    marker = '''    private static void ScheduleCacheTrim()
    {
'''
    helpers = '''    private static bool IsValidImageBytes(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var image = Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: true);
            return image.Width > 0 && image.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void AddMemoryEntry(ConcurrentDictionary<string, byte[]> cache, string key, byte[] bytes)
    {
        cache[key] = bytes;
        if (cache.Count <= MaxMemoryCacheEntries)
            return;

        var removeCount = Math.Max(1, cache.Count - (MaxMemoryCacheEntries * 3 / 4));
        foreach (var oldKey in cache.Keys.Take(removeCount))
            cache.TryRemove(oldKey, out _);
    }

'''
    text = replace_once(text, marker, helpers + marker, "ProfileSelectionPanel cache helpers")
    return text


def patch_toolmanager(text: str) -> str:
    text = replace_once(
        text,
        'new ProductInfoHeaderValue("Plutao", "0.5.0")',
        'new ProductInfoHeaderValue("Plutao", "0.5.6")',
        "ToolManager user agent",
    )

    old_temp = '''    public static string ResolveDefaultTempDirectory()
    {
        const string preferredRoot = @"C:\\PROJETOS\\Plutao-";
        if (Directory.Exists(preferredRoot))
            return Path.Combine(preferredRoot, "Teporarios");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Plutao",
            "Temporarios");
    }
'''
    new_temp = '''    public static string ResolveDefaultTempDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Plutao",
            "Temporarios");
'''
    text = replace_once(text, old_temp, new_temp, "ToolManager temp folder")

    text = replace_once(
        text,
        '''        await DownloadDenoAsync(log, progress, ct);
        if (File.Exists(GalleryDlPath))
            await DownloadGalleryDlAsync(log, progress, ct);
''',
        '''        await DownloadDenoAsync(log, progress, ct);
        await DownloadGalleryDlAsync(log, progress, ct);
''',
        "ToolManager update gallery-dl",
    )

    text = replace_once(
        text,
        'if (File.Exists(GalleryDlPath)) return;',
        'if (IsUsableExecutable(GalleryDlPath)) return;',
        "ToolManager gallery-dl validation",
    )
    text = replace_once(
        text,
        'if (File.Exists(YtDlpPath)) return;',
        'if (IsUsableExecutable(YtDlpPath)) return;',
        "ToolManager yt-dlp validation",
    )
    text = replace_once(
        text,
        'if (File.Exists(FfmpegPath) && File.Exists(FfprobePath)) return;',
        'if (IsUsableExecutable(FfmpegPath) && IsUsableExecutable(FfprobePath)) return;',
        "ToolManager ffmpeg validation",
    )
    text = replace_once(
        text,
        'if (File.Exists(DenoPath)) return;',
        'if (IsUsableExecutable(DenoPath)) return;',
        "ToolManager deno validation",
    )

    text = replace_once(
        text,
        '''            File.Copy(ffmpeg, FfmpegPath, true);
            File.Copy(ffprobe, FfprobePath, true);
            progress?.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", "FFmpeg pronto"));
''',
        '''            var ffmpegStaged = FfmpegPath + ".new";
            var ffprobeStaged = FfprobePath + ".new";
            var ffmpegBackup = FfmpegPath + ".bak";
            var ffprobeBackup = FfprobePath + ".bak";

            File.Copy(ffmpeg, ffmpegStaged, true);
            File.Copy(ffprobe, ffprobeStaged, true);
            if (!IsUsableExecutable(ffmpegStaged) || !IsUsableExecutable(ffprobeStaged))
                throw new InvalidOperationException("O pacote baixado contém executáveis FFmpeg inválidos.");

            TryDeleteFile(ffmpegBackup);
            TryDeleteFile(ffprobeBackup);

            try
            {
                if (File.Exists(FfmpegPath))
                    File.Copy(FfmpegPath, ffmpegBackup, true);
                if (File.Exists(FfprobePath))
                    File.Copy(FfprobePath, ffprobeBackup, true);

                File.Move(ffmpegStaged, FfmpegPath, true);
                File.Move(ffprobeStaged, FfprobePath, true);
            }
            catch
            {
                if (File.Exists(ffmpegBackup))
                    File.Copy(ffmpegBackup, FfmpegPath, true);
                if (File.Exists(ffprobeBackup))
                    File.Copy(ffprobeBackup, FfprobePath, true);
                throw;
            }
            finally
            {
                TryDeleteFile(ffmpegStaged);
                TryDeleteFile(ffprobeStaged);
                TryDeleteFile(ffmpegBackup);
                TryDeleteFile(ffprobeBackup);
            }

            progress?.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", "FFmpeg pronto"));
''',
        "ToolManager atomic FFmpeg pair update",
    )

    marker = '''    private static bool IsYouTubeUrl(string url)
'''
    helper = '''    private static bool IsUsableExecutable(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            var info = new FileInfo(path);
            if (info.Length < 64 * 1024)
                return false;

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
        }
        catch
        {
            return false;
        }
    }

'''
    text = replace_once(text, marker, helper + marker, "ToolManager executable helper")
    return text


def patch_ytdlp(text: str) -> str:
    text = replace_once(
        text,
        "using System.Diagnostics;\n",
        "using System.Collections.Concurrent;\nusing System.Diagnostics;\n",
        "YtDlpRunner using",
    )

    text = replace_once(
        text,
        '''    private string? _currentCompletedFilePath;
    public string? LastCompletedFilePath { get; private set; }
''',
        '''    private string? _currentCompletedFilePath;
    private readonly ConcurrentQueue<string> _completedFilePaths = new();
    private volatile bool _currentRunHadErrors;
    public string? LastCompletedFilePath { get; private set; }
''',
        "YtDlpRunner fields",
    )

    text = replace_once(
        text,
        '''            var result = await AnalyzeYouTubeChannelTabAsync(
                tabUrl,
                tab.Category,
                browserCookies,
                limit,
                log,
                ct);
''',
        '''            var remaining = limit > 0 ? Math.Max(0, limit - combined.Count) : 0;
            if (limit > 0 && remaining == 0)
                break;

            var result = await AnalyzeYouTubeChannelTabAsync(
                tabUrl,
                tab.Category,
                browserCookies,
                remaining,
                log,
                ct);
''',
        "YtDlpRunner YouTube total limit input",
    )
    text = replace_once(
        text,
        '''            combined.AddRange(result.Items);
            log.Report($"[Análise/YouTube] {tab.Category}: {result.Items.Count} item(ns).");
''',
        '''            combined.AddRange(result.Items);
            if (limit > 0 && combined.Count > limit)
                combined.RemoveRange(limit, combined.Count - limit);
            log.Report($"[Análise/YouTube] {tab.Category}: {result.Items.Count} itens.");
            if (limit > 0 && combined.Count >= limit)
                break;
''',
        "YtDlpRunner YouTube total limit output",
    )

    text = replace_once(
        text,
        '''        var finalItems = unique
            .Select((item, index) => item with { Index = index + 1 })
            .ToArray();

        LastAnalyzedProfile = profile is null
            ? null
            : profile with
            {
                ExternalUrl = channelBaseUrl,
                FoundVideos = finalItems.Length
            };

        progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", $"{finalItems.Length} mídia(s) encontrada(s)"));
        log.Report($"[Análise/YouTube] Total: {finalItems.Length} mídia(s) após remover duplicados.");
        return finalItems;
''',
        '''        var finalItems = unique
            .Select((item, index) => item with { Index = index + 1 })
            .ToArray();

        var enrichedItems = await EnrichYouTubeMetadataAsync(
            finalItems,
            browserCookies,
            log,
            progress,
            ct);
        LastAnalyzedProfile = profile is null
            ? null
            : profile with
            {
                ExternalUrl = channelBaseUrl,
                FoundVideos = enrichedItems.Length
            };

        progress.Report(new DownloadProgressInfo(0, 0, 100, "", "", "", $"{enrichedItems.Length} mídias encontradas"));
        log.Report($"[Análise/YouTube] Total: {enrichedItems.Length} mídias após remover duplicados e completar metadados.");
        return enrichedItems;
''',
        "YtDlpRunner YouTube metadata enrichment call",
    )

    text = replace_once(
        text,
        '''    private static string NormalizeYouTubeMediaUrl(string rawUrl, string id)
''',
        '''    private async Task<CollectionMediaItem[]> EnrichYouTubeMetadataAsync(
        IReadOnlyList<CollectionMediaItem> items,
        string browserCookies,
        IProgress<string> log,
        IProgress<DownloadProgressInfo> progress,
        CancellationToken ct)
    {
        if (items.Count == 0)
            return Array.Empty<CollectionMediaItem>();

        var results = items.ToArray();
        var pending = items
            .Select((item, index) => (Item: item, Index: index))
            .Where(x => string.IsNullOrWhiteSpace(x.Item.Duration) ||
                        string.IsNullOrWhiteSpace(x.Item.ThumbnailUrl))
            .ToArray();

        if (pending.Length == 0)
            return results;

        log.Report($"[Análise/YouTube] Completando duração/miniatura de {pending.Length} mídias...");
        progress.Report(new DownloadProgressInfo(0, 0, 82, "", "", "", "Completando metadados do YouTube"));

        var completed = 0;
        var maxParallel = string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase) ? 6 : 3;
        using var gate = new SemaphoreSlim(maxParallel, maxParallel);

        var tasks = pending.Select(async entry =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                results[entry.Index] = await AnalyzeYouTubeItemMetadataAsync(
                    entry.Item,
                    browserCookies,
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                results[entry.Index] = entry.Item;
            }
            finally
            {
                gate.Release();
                var done = Interlocked.Increment(ref completed);
                var percent = 82 + (int)Math.Round(16d * done / pending.Length);
                progress.Report(new DownloadProgressInfo(
                    0,
                    0,
                    Math.Clamp(percent, 82, 98),
                    "",
                    "",
                    entry.Item.Title,
                    $"Metadados do YouTube: {done}/{pending.Length}"));
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private async Task<CollectionMediaItem> AnalyzeYouTubeItemMetadataAsync(
        CollectionMediaItem item,
        string browserCookies,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Url))
            return item;

        var psi = new ProcessStartInfo
        {
            FileName = _tools.YtDlpPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in new[]
                 {
                     "--dump-single-json", "--skip-download", "--no-playlist", "--no-warnings",
                     "--cache-dir", _tools.CacheDirectory,
                     "--js-runtimes", $"deno:{_tools.DenoPath}"
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        if (!string.Equals(browserCookies, "Nenhum", StringComparison.OrdinalIgnoreCase))
        {
            psi.ArgumentList.Add("--cookies-from-browser");
            psi.ArgumentList.Add(browserCookies.ToLowerInvariant());
        }

        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(item.Url);

        using var process = new Process { StartInfo = psi };
        process.Start();
        using var reg = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch { }
        });

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        _ = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return item;

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return item;

            var duration = FormatDuration(ReadDouble(root, "duration"));
            if (string.IsNullOrWhiteSpace(duration))
                duration = item.Duration;

            var thumbnail = ReadThumbnailUrl(root);
            if (string.IsNullOrWhiteSpace(thumbnail))
                thumbnail = item.ThumbnailUrl;

            var date = ReadMediaDate(root);
            if (string.IsNullOrWhiteSpace(date))
                date = item.Date;

            var details = FormatMediaDetails(root);
            if (string.IsNullOrWhiteSpace(details))
                details = item.Details;

            return item with
            {
                Duration = duration,
                ThumbnailUrl = thumbnail,
                Date = date,
                Details = details
            };
        }
        catch (JsonException)
        {
            return item;
        }
    }

    private static string NormalizeYouTubeMediaUrl(string rawUrl, string id)
''',
        "YtDlpRunner YouTube metadata enrichment methods",
    )

    text = replace_once(
        text,
        '            ExtractInstagramVideoItems(stdout, items, seen, limit);',
        '            ExtractInstagramVideoItems(stdout, items, seen, limit, target.Label);',
        "YtDlpRunner Instagram category call",
    )
    text = replace_once(
        text,
        '''    private static void ExtractInstagramVideoItems(
        string stdout,
        List<CollectionMediaItem> items,
        HashSet<string> seen,
        int limit)
''',
        '''    private static void ExtractInstagramVideoItems(
        string stdout,
        List<CollectionMediaItem> items,
        HashSet<string> seen,
        int limit,
        string category)
''',
        "YtDlpRunner Instagram category signature",
    )

    old_instagram_item = '''                var details = FormatInstagramMediaDetails(metadata);
                items.Add(new CollectionMediaItem(
                    items.Count + 1,
                    shortcode,
                    title,
                    FormatDuration(duration),
                    postUrl,
                    thumbnail,
                    date,
                    details));
'''
    new_instagram_item = '''                var details = FormatInstagramMediaDetails(metadata);
                items.Add(new CollectionMediaItem(
                    items.Count + 1,
                    shortcode,
                    title,
                    FormatDuration(duration),
                    postUrl,
                    thumbnail,
                    date,
                    details,
                    category));
'''
    text = replace_once(text, old_instagram_item, new_instagram_item, "YtDlpRunner Instagram item category")

    text = text.replace(
        "tente ANALISAR CONTA/PÁGINA novamente.",
        "clique em TENTAR NOVAMENTE.",
    )

    text = replace_once(
        text,
        '''        var username = ReadString(root, "uploader")
                       ?? ReadString(root, "channel")
                       ?? ReadString(root, "playlist_uploader")
                       ?? ReadString(root, "uploader_id")
                       ?? string.Empty;
''',
        '''        var username = ReadString(root, "uploader_id")
                       ?? ReadString(root, "channel_id")
                       ?? ReadString(root, "uploader")
                       ?? ReadString(root, "channel")
                       ?? ReadString(root, "playlist_uploader")
                       ?? string.Empty;
''',
        "YtDlpRunner profile username",
    )

    text = replace_once(
        text,
        '''        var views = ReadLong(element, "view_count");
        var likes = ReadLong(element, "like_count");
        var width = ReadInt(element, "width");
        var height = ReadInt(element, "height");
        if (views.HasValue) details.Add($"{views.Value:N0} visualizações");
        if (likes.HasValue) details.Add($"{likes.Value:N0} curtidas");
''',
        '''        var views = ReadLong(element, "view_count");
        var likes = ReadLong(element, "like_count");
        var comments = ReadLong(element, "comment_count");
        var width = ReadInt(element, "width");
        var height = ReadInt(element, "height");
        if (views.HasValue) details.Add($"{views.Value:N0} visualizações");
        if (likes.HasValue) details.Add($"{likes.Value:N0} curtidas");
        if (comments.HasValue) details.Add($"{comments.Value:N0} comentários");
''',
        "YtDlpRunner comments",
    )

    text = replace_once(
        text,
        '''        var likes = ReadLong(metadata, "likes") ?? ReadLong(metadata, "like_count");
        var width = ReadInt(metadata, "width_original") ?? ReadInt(metadata, "width");
        var height = ReadInt(metadata, "height_original") ?? ReadInt(metadata, "height");
        if (views.HasValue) details.Add($"{views.Value:N0} visualizações");
        if (likes.HasValue) details.Add($"{likes.Value:N0} curtidas");
''',
        '''        var likes = ReadLong(metadata, "likes") ?? ReadLong(metadata, "like_count");
        var comments = ReadLong(metadata, "comments") ?? ReadLong(metadata, "comment_count");
        var width = ReadInt(metadata, "width_original") ?? ReadInt(metadata, "width");
        var height = ReadInt(metadata, "height_original") ?? ReadInt(metadata, "height");
        if (views.HasValue) details.Add($"{views.Value:N0} visualizações");
        if (likes.HasValue) details.Add($"{likes.Value:N0} curtidas");
        if (comments.HasValue) details.Add($"{comments.Value:N0} comentários");
''',
        "YtDlpRunner Instagram comments",
    )

    text = replace_once(
        text,
        '''        _currentCompletedFilePath = null;
        var timing = new RunTiming();
''',
        '''        _currentCompletedFilePath = null;
        while (_completedFilePaths.TryDequeue(out _)) { }
        _currentRunHadErrors = false;
        var timing = new RunTiming();
''',
        "YtDlpRunner reset current paths",
    )

    text = replace_once(
        text,
        '''        if (singleMedia && CanReuseCompletedFile(options) && AppCache.TryGetReusableDownload(reuseKey, out var reusablePath))
        {
            var cacheUsable = true;

            // Nunca devolve silenciosamente um MP4 antigo em AV1/VP9/Opus.
            // Antes de reutilizar o arquivo, valida a mesma regra aplicada aos downloads novos.
            if (ShouldGuaranteeCompatibleMp4(options))
''',
        '''        if (singleMedia && CanReuseCompletedFile(options) && AppCache.TryGetReusableDownload(reuseKey, out var reusablePath))
        {
            var cacheUsable = await IsReusableFileHealthyAsync(reusablePath, ct);
            if (!cacheUsable)
            {
                AppCache.InvalidateReusableDownload(reuseKey);
                log.Report("[Cache] Arquivo salvo está incompleto ou corrompido; será baixado novamente.");
            }

            // Nunca devolve silenciosamente um MP4 antigo em AV1/VP9/Opus.
            // Antes de reutilizar o arquivo, valida a mesma regra aplicada aos downloads novos.
            if (cacheUsable && ShouldGuaranteeCompatibleMp4(options))
''',
        "YtDlpRunner reusable cache guard",
    )

    text = replace_once(
        text,
        '''        var exitCode = _process.ExitCode;
        var processEndedAt = timing.Total.Elapsed;
''',
        '''        var exitCode = _process.ExitCode;
        if (!singleMedia && exitCode == 0 && _currentRunHadErrors)
            exitCode = 1;
        var processEndedAt = timing.Total.Elapsed;
''',
        "YtDlpRunner collection partial failure",
    )

    old_final = '''        string? completedPath = null;
        if (exitCode == 0)
        {
            completedPath = ResolveCompletedMediaPath(options, url, processStartedUtc, uniqueSuffix);
            if (string.IsNullOrWhiteSpace(completedPath) || !File.Exists(completedPath))
            {
                log.Report("[Arquivo] ERRO: o yt-dlp terminou, mas o Plutao não conseguiu localizar o arquivo final.");
                log.Report("[Arquivo] O download não será marcado como concluído para evitar entregar um MP4 sem validação de codec.");
                return 2;
            }

            _currentCompletedFilePath = completedPath;
            LastCompletedFilePath = completedPath;
            log.Report($"[Arquivo final] {completedPath}");
        }

        if (exitCode == 0 && ShouldGuaranteeCompatibleMp4(options))
        {
            try
            {
                var compatibilityTimer = Stopwatch.StartNew();
                await EnsureCompatibleMp4Async(completedPath!, options, itemIndex, itemCount, log, progress, ct);
                compatibilityTimer.Stop();
                compatibilityElapsed = compatibilityTimer.Elapsed;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Report("[Compatibilidade] ERRO ao validar/converter o MP4: " + ex.Message);
                return 2;
            }
        }
'''
    new_final = '''        string? completedPath = null;
        var capturedPaths = _completedFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (exitCode == 0)
        {
            completedPath = capturedPaths.LastOrDefault()
                            ?? ResolveCompletedMediaPath(options, url, processStartedUtc, uniqueSuffix);
            if (string.IsNullOrWhiteSpace(completedPath) || !File.Exists(completedPath))
            {
                log.Report("[Arquivo] ERRO: o yt-dlp terminou, mas o Plutao não conseguiu localizar o arquivo final.");
                log.Report("[Arquivo] O download não será marcado como concluído sem validação do arquivo.");
                return 2;
            }

            _currentCompletedFilePath = completedPath;
            log.Report($"[Arquivo final] {completedPath}");
        }

        if (exitCode == 0 && ShouldGuaranteeCompatibleMp4(options))
        {
            try
            {
                var compatibilityTimer = Stopwatch.StartNew();
                var pathsToValidate = capturedPaths.Length > 0
                    ? capturedPaths
                    : new[] { completedPath! };

                foreach (var pathToValidate in pathsToValidate.Where(File.Exists))
                    await EnsureCompatibleMp4Async(pathToValidate, options, itemIndex, itemCount, log, progress, ct);

                compatibilityTimer.Stop();
                compatibilityElapsed = compatibilityTimer.Elapsed;
                _currentCompletedFilePath = completedPath;
                LastCompletedFilePath = completedPath;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LastCompletedFilePath = null;
                log.Report("[Compatibilidade] ERRO ao validar/converter o MP4: " + ex.Message);
                return 2;
            }
        }
        else if (exitCode == 0)
        {
            LastCompletedFilePath = completedPath;
        }
'''
    text = replace_once(text, old_final, new_final, "YtDlpRunner final validation")

    text = replace_once(
        text,
        '''        var line = AnsiRegex.Replace(rawLine, string.Empty).TrimEnd();

        if (line.StartsWith(FilePrefix, StringComparison.Ordinal))
''',
        '''        var line = AnsiRegex.Replace(rawLine, string.Empty).TrimEnd();
        if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            _currentRunHadErrors = true;

        if (line.StartsWith(FilePrefix, StringComparison.Ordinal))
''',
        "YtDlpRunner error tracking",
    )
    text = replace_once(
        text,
        '''                _currentCompletedFilePath = path;
                LastCompletedFilePath = path;
                log.Report($"[Arquivo] {path}");
''',
        '''                _currentCompletedFilePath = path;
                _completedFilePaths.Enqueue(path);
                log.Report($"[Arquivo] {path}");
''',
        "YtDlpRunner capture after_move",
    )
    text = replace_once(
        text,
        '''            _currentCompletedFilePath = candidate;
            LastCompletedFilePath = candidate;
''',
        '''            _currentCompletedFilePath = candidate;
            _completedFilePaths.Enqueue(candidate);
''',
        "YtDlpRunner capture fallback",
    )

    text = replace_once(
        text,
        '''    private static bool CanReuseCompletedFile(DownloadOptions options)
        => !options.SaveThumbnail &&
''',
        '''    private static bool CanReuseCompletedFile(DownloadOptions options)
        => options.ExistingFileBehavior != ExistingFileBehavior.Replace &&
           !options.SaveThumbnail &&
''',
        "YtDlpRunner replace cache",
    )

    marker = '''    private IEnumerable<string> BuildArguments(string url, DownloadOptions o, string uniqueSuffix)
'''
    helper = '''    private async Task<bool> IsReusableFileHealthyAsync(string path, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 1024)
                return false;

            var videoCodec = await ProbeCodecAsync(path, "v:0", ct);
            var audioCodec = await ProbeCodecAsync(path, "a:0", ct);
            return !string.IsNullOrWhiteSpace(videoCodec) || !string.IsNullOrWhiteSpace(audioCodec);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

'''
    text = replace_once(text, marker, helper + marker, "YtDlpRunner reusable file helper")

    text = replace_once(
        text,
        '''        if (container.Equals("webm", StringComparison.OrdinalIgnoreCase))
        {
            if (height.HasValue)
                return $"bv*{exact}[ext=webm]+ba[ext=webm]/b[ext=webm]{exact}/bv*{limit}[ext=webm]+ba[ext=webm]/b[ext=webm]{limit}/bv*{limit}+ba/b{limit}/b";
            return "bv*[ext=webm]+ba[ext=webm]/b[ext=webm]/bv*+ba/b";
        }
''',
        '''        if (container.Equals("webm", StringComparison.OrdinalIgnoreCase))
        {
            if (height.HasValue)
                return $"bv*[ext=webm]{exact}+ba[ext=webm]/b[ext=webm]{exact}/bv*[ext=webm]{limit}+ba[ext=webm]/b[ext=webm]{limit}";
            return "bv*[ext=webm]+ba[ext=webm]/b[ext=webm]";
        }
''',
        "YtDlpRunner WebM selector",
    )

    text = replace_once(
        text,
        '''    private static string ReadThumbnailUrl(JsonElement element)
    {
        var direct = ReadString(element, "thumbnail");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("thumbnails", out var thumbnails) &&
            thumbnails.ValueKind == JsonValueKind.Array)
        {
            string result = string.Empty;
            foreach (var thumb in thumbnails.EnumerateArray())
            {
                if (thumb.ValueKind != JsonValueKind.Object) continue;
                var value = ReadString(thumb, "url");
                if (!string.IsNullOrWhiteSpace(value)) result = value;
            }
            return result;
        }

        return string.Empty;
    }
''',
        '''    private static string ReadThumbnailUrl(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("thumbnails", out var thumbnails) &&
            thumbnails.ValueKind == JsonValueKind.Array)
        {
            const int targetWidth = 480;
            string bestUrl = string.Empty;
            var bestScore = int.MaxValue;

            foreach (var thumb in thumbnails.EnumerateArray())
            {
                if (thumb.ValueKind != JsonValueKind.Object)
                    continue;

                var value = ReadString(thumb, "url");
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                var width = ReadInt(thumb, "width");
                var score = width.HasValue && width.Value > 0
                    ? Math.Abs(width.Value - targetWidth)
                    : 100_000;

                if (score < bestScore)
                {
                    bestScore = score;
                    bestUrl = value;
                }
            }

            if (!string.IsNullOrWhiteSpace(bestUrl))
                return bestUrl;
        }

        return ReadString(element, "thumbnail") ?? string.Empty;
    }
''',
        "YtDlpRunner thumbnail size selection",
    )

    text = replace_once(
        text,
        '''        var videoCodec = await ProbeCodecAsync(path, "v:0", ct);
        var audioCodec = await ProbeCodecAsync(path, "a:0", ct);
        var actualHeight = await ProbeHeightAsync(path, ct);
        var requestedHeight = ParseQualityHeight(options.Quality);

        // Regra automática:
        // até 1080p -> H.264/AAC;
        // acima de 1080p -> HEVC/H.265 + AAC.
        // Em "Melhor", a altura real do arquivo decide o codec final.
        var effectiveHeight = actualHeight > 0 ? actualHeight : requestedHeight;
''',
        '''        var videoCodec = await ProbeCodecAsync(path, "v:0", ct);
        var audioCodec = await ProbeCodecAsync(path, "a:0", ct);
        var (actualWidth, actualHeight) = await ProbeDimensionsAsync(path, ct);
        var requestedHeight = ParseQualityHeight(options.Quality);

        // Usa o menor lado como classe de resolução. Assim um Short 1080x1920
        // continua sendo tratado como 1080p, enquanto 2560x1440 vira 1440p.
        var actualResolution = actualWidth > 0 && actualHeight > 0
            ? Math.Min(actualWidth, actualHeight)
            : actualHeight;
        var effectiveHeight = actualResolution > 0 ? actualResolution : requestedHeight;
''',
        "YtDlpRunner portrait resolution",
    )

    old_move = '''            File.Move(tempPath, path, true);

            // Nunca confia apenas no exit code do FFmpeg. O arquivo final precisa
            // realmente estar no codec prometido antes de ser marcado como concluído.
            var finalVideoCodec = await ProbeCodecAsync(path, "v:0", ct);
            var finalAudioCodec = await ProbeCodecAsync(path, "a:0", ct);
            var finalHeight = await ProbeHeightAsync(path, ct);
            var finalTargetHevc = (finalHeight > 0 ? finalHeight : effectiveHeight) > 1080;
            var finalVideoOk = finalTargetHevc
                ? string.Equals(finalVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(finalVideoCodec, "h265", StringComparison.OrdinalIgnoreCase)
                : string.Equals(finalVideoCodec, "h264", StringComparison.OrdinalIgnoreCase);
            var finalAudioOk = string.IsNullOrWhiteSpace(finalAudioCodec) ||
                               string.Equals(finalAudioCodec, "aac", StringComparison.OrdinalIgnoreCase);

            if (!finalVideoOk || !finalAudioOk)
                throw new InvalidOperationException(
                    $"Verificação final falhou: vídeo={EmptyAsUnknown(finalVideoCodec)}, áudio={EmptyAsUnknown(finalAudioCodec)}.");

            _currentCompletedFilePath = path;
            LastCompletedFilePath = path;
            log.Report($"[Compatibilidade] Verificação final: vídeo={EmptyAsUnknown(finalVideoCodec)}, áudio={EmptyAsUnknown(finalAudioCodec)}, altura={(finalHeight > 0 ? finalHeight + "p" : "desconhecida")}.");
'''
    new_move = '''            var tempVideoCodec = await ProbeCodecAsync(tempPath, "v:0", ct);
            var tempAudioCodec = await ProbeCodecAsync(tempPath, "a:0", ct);
            var (tempWidth, tempHeight) = await ProbeDimensionsAsync(tempPath, ct);
            var tempResolution = tempWidth > 0 && tempHeight > 0 ? Math.Min(tempWidth, tempHeight) : tempHeight;
            var tempTargetHevc = (tempResolution > 0 ? tempResolution : effectiveHeight) > 1080;
            var tempVideoOk = tempTargetHevc
                ? string.Equals(tempVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(tempVideoCodec, "h265", StringComparison.OrdinalIgnoreCase)
                : string.Equals(tempVideoCodec, "h264", StringComparison.OrdinalIgnoreCase);
            var tempAudioOk = string.IsNullOrWhiteSpace(tempAudioCodec) ||
                              string.Equals(tempAudioCodec, "aac", StringComparison.OrdinalIgnoreCase);

            if (!tempVideoOk || !tempAudioOk)
                throw new InvalidOperationException(
                    $"Verificação do arquivo convertido falhou: vídeo={EmptyAsUnknown(tempVideoCodec)}, áudio={EmptyAsUnknown(tempAudioCodec)}.");

            // O original só é substituído depois que o temporário passou no ffprobe.
            File.Move(tempPath, path, true);

            var finalVideoCodec = await ProbeCodecAsync(path, "v:0", ct);
            var finalAudioCodec = await ProbeCodecAsync(path, "a:0", ct);
            var (finalWidth, finalHeight) = await ProbeDimensionsAsync(path, ct);
            var finalResolution = finalWidth > 0 && finalHeight > 0 ? Math.Min(finalWidth, finalHeight) : finalHeight;
            var finalTargetHevc = (finalResolution > 0 ? finalResolution : effectiveHeight) > 1080;
            var finalVideoOk = finalTargetHevc
                ? string.Equals(finalVideoCodec, "hevc", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(finalVideoCodec, "h265", StringComparison.OrdinalIgnoreCase)
                : string.Equals(finalVideoCodec, "h264", StringComparison.OrdinalIgnoreCase);
            var finalAudioOk = string.IsNullOrWhiteSpace(finalAudioCodec) ||
                               string.Equals(finalAudioCodec, "aac", StringComparison.OrdinalIgnoreCase);

            if (!finalVideoOk || !finalAudioOk)
            {
                LastCompletedFilePath = null;
                throw new InvalidOperationException(
                    $"Verificação final falhou: vídeo={EmptyAsUnknown(finalVideoCodec)}, áudio={EmptyAsUnknown(finalAudioCodec)}.");
            }

            _currentCompletedFilePath = path;
            LastCompletedFilePath = path;
            log.Report($"[Compatibilidade] Verificação final: vídeo={EmptyAsUnknown(finalVideoCodec)}, áudio={EmptyAsUnknown(finalAudioCodec)}, resolução={(finalResolution > 0 ? finalResolution + "p" : "desconhecida")}.");
'''
    text = replace_once(text, old_move, new_move, "YtDlpRunner safe MP4 replacement")

    marker2 = '''    private async Task<int> ProbeHeightAsync(string path, CancellationToken ct)
'''
    dimensions = '''    private async Task<(int Width, int Height)> ProbeDimensionsAsync(string path, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _tools.FfprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "csv=p=0:s=x", path })
            psi.ArgumentList.Add(arg);

        using var probe = new Process { StartInfo = psi };
        probe.Start();
        var stdoutTask = probe.StandardOutput.ReadToEndAsync();
        var stderrTask = probe.StandardError.ReadToEndAsync();
        await probe.WaitForExitAsync(ct);
        var stdout = (await stdoutTask).Trim();
        _ = await stderrTask;

        if (probe.ExitCode != 0)
            return (0, 0);

        var first = stdout.Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var parts = first?.Split('x', 2);
        return parts is { Length: 2 } &&
               int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) &&
               int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            ? (width, height)
            : (0, 0);
    }

'''
    text = replace_once(text, marker2, dimensions + marker2, "YtDlpRunner dimensions probe")

    text = text.replace("uma sessao.", "uma sessão.")
    text = text.replace("Baixar pagina/conta/canal/perfil", "Baixar página/conta/canal/perfil")
    text = text.replace("frequencia", "frequência")
    text = text.replace("item(ns)", "itens")
    text = text.replace("mídia(s)", "mídias")
    text = text.replace("vídeo(s)", "vídeos")
    return text


def patch_changelog(text: str) -> str:
    if not text.startswith("# Changelog\n\n"):
        raise RuntimeError("CHANGELOG.md não corresponde à base esperada.")

    section = '''# Changelog

## 0.5.6

- Corrige colisão de cache entre playlists diferentes do YouTube.
- O modo **Substituir** deixa de reaproveitar silenciosamente um arquivo do cache.
- Arquivos reutilizados passam por verificação básica com `ffprobe`; cache inválido é descartado.
- **ABRIR ARQUIVO** só é liberado depois da validação final do arquivo.
- Conversões MP4 validam o temporário antes de substituir o original.
- Vídeos verticais como 1080x1920 passam a ser tratados como 1080p; 1440p/4K continuam usando HEVC.
- Coleções MP4 validam os caminhos finais capturados, em vez de validar apenas o último arquivo.
- Erros internos de coleções deixam de ser mascarados pelo `--ignore-errors`.
- O limite de canal do YouTube passa a valer para o total de Vídeos + Shorts + Lives.
- Instagram ganha categorias **Reels** e **Posts**.
- Comentários são exibidos quando a plataforma fornece a contagem.
- Corrige `@` duplicado e melhora a escolha do identificador de canais.
- Miniaturas do YouTube ganham fallback por ID; caches de imagem corrompidos são descartados.
- Cache de miniaturas em RAM passa a ter limite.
- Corrige o progresso em filas que misturam vários links e coleções.
- Durante uma tarefa, opções que alterariam a execução atual ficam bloqueadas.
- Análise cancelada ou vazia passa a mostrar estado correto e **TENTAR NOVAMENTE**.
- Resultados ocultos podem ser mostrados novamente.
- Gaveta de configurações, prévia e colunas da lista recebem ajustes de geometria.
- **Atualizar componentes** também instala/atualiza `gallery-dl`.
- Executáveis de componentes truncados passam a ser detectados.
- Remove o caminho temporário especial de desenvolvimento e usa `%LOCALAPPDATA%\\Plutao\\Temporarios`.
- User-Agent interno e versão do aplicativo atualizados para 0.5.6.

'''
    return section + text[len("# Changelog\n\n"):]


def patch_readme(text: str) -> str:
    text = text.replace(
        "## v0.5.5 — fluxo automático e painéis laterais",
        "## v0.5.6 — correções de estabilidade e interface",
        1,
    )
    text = text.replace(
        "A v0.5.5 mantém as melhorias anteriores:",
        "A v0.5.6 mantém as melhorias anteriores e corrige cache, validação final, progresso e geometria:",
        1,
    )
    text = text.replace("estado pública/privada/verificada", "conta pública/privada/verificada")
    return text


def validate_base(repo: Path, originals: dict[str, str]) -> None:
    if "<Version>0.5.5</Version>" not in originals["csproj"]:
        raise RuntimeError("Plutao.csproj não está na v0.5.5.")

    if "v0.5.5" not in originals["mainform"]:
        raise RuntimeError("MainForm.cs não corresponde à v0.5.5.")

    git = shutil.which("git")
    if git:
        head_result = run([git, "rev-parse", "HEAD"], repo, check=False)
        if head_result.returncode == 0:
            head = head_result.stdout.strip()
            if head != BASE_SHA:
                raise RuntimeError(
                    f"Base incorreta: HEAD atual {head}. "
                    f"Esperado {BASE_SHA} (v0.5.5)."
                )

            dirty = run([git, "status", "--porcelain"], repo).stdout.strip()
            if dirty:
                raise RuntimeError(
                    "O repositório possui alterações locais. "
                    "A v0.5.6 não será aplicada sobre uma base modificada."
                )


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Atualiza Plutao v0.5.5 para v0.5.6, cria backup e compila somente após validar os patches."
    )
    parser.add_argument(
        "repo",
        nargs="?",
        default=r"C:\PROJETOS\Plutao-\Plutao",
        help=r"Pasta do repositório. Padrão: C:\PROJETOS\Plutao-\Plutao",
    )
    parser.add_argument(
        "--no-build",
        action="store_true",
        help="Aplica o código, mas não compila e não gera os ZIPs finais.",
    )
    args = parser.parse_args()

    repo = Path(args.repo).resolve()
    if not repo.exists():
        print(f"ERRO: repositório não encontrado: {repo}", file=sys.stderr)
        return 2

    files = {
        "appcache": repo / "src/Plutao/AppCache.cs",
        "progress": repo / "src/Plutao/DownloadProgressInfo.cs",
        "mainform": repo / "src/Plutao/MainForm.cs",
        "profile": repo / "src/Plutao/ProfileSelectionPanel.cs",
        "tools": repo / "src/Plutao/ToolManager.cs",
        "runner": repo / "src/Plutao/YtDlpRunner.cs",
        "csproj": repo / "src/Plutao/Plutao.csproj",
        "changelog": repo / "CHANGELOG.md",
        "readme": repo / "README.md",
    }

    missing = [str(path) for path in files.values() if not path.exists()]
    if missing:
        print("ERRO: arquivos obrigatórios ausentes:\n" + "\n".join(missing), file=sys.stderr)
        return 2

    originals = {key: path.read_text(encoding="utf-8") for key, path in files.items()}

    try:
        validate_base(repo, originals)

        # Todas as alterações são calculadas em memória antes de gravar qualquer arquivo.
        updated = dict(originals)
        updated["appcache"] = patch_appcache(updated["appcache"])
        updated["progress"] = patch_progress(updated["progress"])
        updated["mainform"] = patch_mainform(updated["mainform"])
        updated["profile"] = patch_profile_panel(updated["profile"])
        updated["tools"] = patch_toolmanager(updated["tools"])
        updated["runner"] = patch_ytdlp(updated["runner"])
        updated["csproj"] = replace_once(
            updated["csproj"],
            "<Version>0.5.5</Version>",
            "<Version>0.5.6</Version>",
            "Plutao.csproj version",
        )
        updated["changelog"] = patch_changelog(updated["changelog"])
        updated["readme"] = patch_readme(updated["readme"])

    except Exception as exc:
        print(f"ERRO NA VALIDAÇÃO: {exc}", file=sys.stderr)
        print("Nenhum arquivo foi alterado.", file=sys.stderr)
        return 3

    parent = repo.parent
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_zip = parent / f"Plutao-v0.5.5-backup-{stamp}.zip"
    source_zip = parent / f"Plutao-v{VERSION}-source.zip"
    app_zip = parent / f"Plutao-v{VERSION}-win-x64.zip"
    release_dir = repo / "release"

    picker = repo / "src/Plutao/CollectionPickerForm.cs"
    picker_original = picker.read_bytes() if picker.exists() else None

    try:
        print("[1/5] Criando backup completo da fonte v0.5.5...")
        zip_tree(repo, backup_zip, exclude_build=True)

        print("[2/5] Aplicando correções v0.5.6...")
        for key, path in files.items():
            path.write_text(updated[key], encoding="utf-8", newline="\n")

        if args.no_build:
            print("\nCódigo v0.5.6 aplicado com --no-build.")
            print(f"Backup da v0.5.5: {backup_zip}")
            print("Nenhum ZIP final foi criado, pois o build não foi executado.")
            return 0

        dotnet = shutil.which("dotnet")
        if not dotnet:
            raise RuntimeError(
                ".NET SDK não encontrado no PATH. "
                "Instale/use o .NET 8 SDK e execute o atualizador novamente."
            )

        print("[3/5] Compilando win-x64 self-contained...")
        if release_dir.exists():
            shutil.rmtree(release_dir)

        publish = run(
            [
                dotnet,
                "publish",
                str(repo / "src/Plutao/Plutao.csproj"),
                "-c",
                "Release",
                "-r",
                "win-x64",
                "--self-contained",
                "true",
                "/p:PublishSingleFile=true",
                "/p:IncludeNativeLibrariesForSelfExtract=true",
                "-o",
                str(release_dir),
            ],
            repo,
        )
        if publish.stdout.strip():
            print(publish.stdout)

        exe = release_dir / "Plutao.exe"
        if not exe.exists() or exe.stat().st_size <= 0:
            raise RuntimeError("O build terminou sem gerar um Plutao.exe válido.")

        # O arquivo legado só é removido depois que a compilação já passou.
        if picker.exists():
            picker.unlink()

        print("[4/5] Gerando pacote do aplicativo...")
        if app_zip.exists():
            app_zip.unlink()
        with zipfile.ZipFile(app_zip, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.write(exe, "Plutao.exe")

        print("[5/5] Gerando ZIP completo do código-fonte...")
        zip_tree(repo, source_zip, exclude_build=True)

        print("\nATUALIZAÇÃO CONCLUÍDA COM SUCESSO")
        print(f"EXE: {exe}")
        print(f"ZIP aplicativo: {app_zip}")
        print(f"ZIP código-fonte completo: {source_zip}")
        print(f"Backup v0.5.5: {backup_zip}")
        print("\nO commit/base v0.5.5 não é apagado pelo atualizador.")
        return 0

    except Exception as exc:
        print(f"\nERRO: {exc}", file=sys.stderr)
        print("Restaurando a fonte v0.5.5...", file=sys.stderr)

        for key, path in files.items():
            try:
                path.write_text(originals[key], encoding="utf-8", newline="\n")
            except Exception:
                pass

        if picker_original is not None:
            try:
                picker.parent.mkdir(parents=True, exist_ok=True)
                picker.write_bytes(picker_original)
            except Exception:
                pass

        # Nunca deixa ZIP final de uma compilação que falhou.
        for candidate in (app_zip, source_zip):
            try:
                if candidate.exists():
                    candidate.unlink()
            except Exception:
                pass

        print(f"Backup preservado em: {backup_zip}", file=sys.stderr)
        return 4


if __name__ == "__main__":    raise SystemExit(main())