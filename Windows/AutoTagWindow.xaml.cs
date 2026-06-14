using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FileTagger.Services;
using Microsoft.Win32;

namespace FileTagger.Windows
{
    public class LlamaSettings
    {
        public string LlamaPath { get; set; } = string.Empty;
        public string ModelPath { get; set; } = string.Empty;
        public string MmprojPath { get; set; } = string.Empty;

        private static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileTagger", "llama-settings.json");

        public static LlamaSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<LlamaSettings>(json) ?? new LlamaSettings();
                }
            }
            catch { }
            return new LlamaSettings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }
    }

    public class OllamaSettings
    {
        public string Host { get; set; } = "http://localhost:11434";
        public string Model { get; set; } = "huihui_ai/gemma-4-abliterated:12b";
        public int NumCtx { get; set; } = 65536;

        private static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileTagger", "ollama-settings.json");

        public static OllamaSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<OllamaSettings>(json) ?? new OllamaSettings();
                }
            }
            catch { }
            return new OllamaSettings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFilePath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }
    }

    public partial class AutoTagWindow : Window
    {
        private const string AiTaggedUnchecked = "AI-Tagged-Unchecked";
        private const string BasicPromt = "List sexual content and sexual activities tags for this image";

        private static readonly HashSet<string> ImageExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp" };

        private static readonly HttpClient OllamaHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

        private CancellationTokenSource _cts;
        private bool _isRunning;

        public AutoTagWindow()
        {
            InitializeComponent();
            LoadSettings();
            LoadDirectories();

            AllDirectoriesRadio.Checked += (_, _) =>
            {
                DirectoryComboBox.IsEnabled = false;
                SpecificImageTextBox.IsEnabled = false;
                BrowseImageButton.IsEnabled = false;
            };
            SpecificDirectoryRadio.Checked += (_, _) =>
            {
                DirectoryComboBox.IsEnabled = true;
                SpecificImageTextBox.IsEnabled = false;
                BrowseImageButton.IsEnabled = false;
            };
            SpecificImageRadio.Checked += (_, _) =>
            {
                DirectoryComboBox.IsEnabled = false;
                SpecificImageTextBox.IsEnabled = true;
                BrowseImageButton.IsEnabled = true;
            };
        }

        private void LoadSettings()
        {
            var s = LlamaSettings.Load();
            LlamaPathTextBox.Text = s.LlamaPath;
            ModelPathTextBox.Text = s.ModelPath;
            MmprojPathTextBox.Text = s.MmprojPath;

            var o = OllamaSettings.Load();
            OllamaHostTextBox.Text = o.Host;
            OllamaModelTextBox.Text = o.Model;
            OllamaNumCtxTextBox.Text = o.NumCtx.ToString();
        }

        private void SaveSettings()
        {
            new LlamaSettings
            {
                LlamaPath = LlamaPathTextBox.Text.Trim(),
                ModelPath = ModelPathTextBox.Text.Trim(),
                MmprojPath = MmprojPathTextBox.Text.Trim()
            }.Save();
        }

        private void SaveOllamaSettings()
        {
            int.TryParse(OllamaNumCtxTextBox.Text.Trim(), out var numCtx);
            if (numCtx <= 0)
                numCtx = 65536;

            new OllamaSettings
            {
                Host = OllamaHostTextBox.Text.Trim(),
                Model = OllamaModelTextBox.Text.Trim(),
                NumCtx = numCtx
            }.Save();
        }

        private void LoadDirectories()
        {
            var dirs = DatabaseManager.Instance.GetAllActiveDirectories();
            DirectoryComboBox.ItemsSource = dirs;
            if (dirs.Any())
                DirectoryComboBox.SelectedIndex = 0;
        }

        #region Browse Buttons

        private void BrowseLlama_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select llama-cli executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
                LlamaPathTextBox.Text = dlg.FileName;
        }

        private void BrowseModel_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select LLaMA model file (.gguf)",
                Filter = "GGUF model files (*.gguf)|*.gguf|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
                ModelPathTextBox.Text = dlg.FileName;
        }

        private void BrowseMmproj_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select mmproj file (.gguf)",
                Filter = "GGUF model files (*.gguf)|*.gguf|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
                MmprojPathTextBox.Text = dlg.FileName;
        }

        private void BrowseImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select image file",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == true)
                SpecificImageTextBox.Text = dlg.FileName;
        }

        #endregion

        #region Start / Cancel

        private bool TryCollectImageFiles(out List<string> imageFiles, bool includeTagged = false)
        {
            imageFiles = new List<string>();

            if (SpecificImageRadio.IsChecked == true)
            {
                var imagePath = SpecificImageTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                {
                    MessageBox.Show("Please select a valid image file.", "No Image Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                if (!ImageExtensions.Contains(Path.GetExtension(imagePath)))
                {
                    MessageBox.Show("The selected file is not a supported image type.", "Invalid File",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                imageFiles = new List<string> { imagePath };
                return true;
            }

            List<string> targetDirs;
            if (AllDirectoriesRadio.IsChecked == true)
            {
                targetDirs = DatabaseManager.Instance.GetAllActiveDirectories();
            }
            else
            {
                if (DirectoryComboBox.SelectedItem is string selectedDir)
                    targetDirs = new List<string> { selectedDir };
                else
                {
                    MessageBox.Show("Please select a directory.", "No Selection",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }

            if (!targetDirs.Any())
            {
                MessageBox.Show("No watched directories configured.", "No Directories",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            imageFiles = targetDirs
                .Where(Directory.Exists)
                .SelectMany(d => Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f)))
                .Where(f =>
                {
                    if (includeTagged)
                        return true;

                    var tags = DatabaseManager.Instance.GetTagsForFile(f);
                    return tags.Count == 0 ||
                           tags.All(t => t.Equals(AiTaggedUnchecked, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();

            if (!imageFiles.Any())
            {
                var message = includeTagged
                    ? "No image files found in the selected directories."
                    : "No untagged image files found in the selected directories.";
                MessageBox.Show(message, "No Images",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            return true;
        }

        private void BeginRun(int imageCount)
        {
            _isRunning = true;
            _cts = new CancellationTokenSource();
            StartButton.IsEnabled = false;
            TestOllamaButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            LogTextBox.Clear();
            ProgressBar.Minimum = 0;
            ProgressBar.Maximum = imageCount;
            ProgressBar.Value = 0;
            ProgressLabel.Text = $"Processed 0 / {imageCount}";
        }

        private void EndRun()
        {
            _isRunning = false;
            Dispatcher.Invoke(() =>
            {
                StartButton.IsEnabled = true;
                TestOllamaButton.IsEnabled = true;
                CancelButton.IsEnabled = false;
            });
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
                return;

            var llamaPath = LlamaPathTextBox.Text.Trim();
            var modelPath = ModelPathTextBox.Text.Trim();
            var mmprojPath = MmprojPathTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(llamaPath) || !File.Exists(llamaPath))
            {
                MessageBox.Show("Please specify a valid path to llama-cli.exe.", "Missing Setting",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                MessageBox.Show("Please specify a valid model (.gguf) file path.", "Missing Setting",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(mmprojPath) || !File.Exists(mmprojPath))
            {
                MessageBox.Show("Please specify a valid mmproj (.gguf) file path.", "Missing Setting",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveSettings();

            if (!TryCollectImageFiles(out var imageFiles))
                return;

            BeginRun(imageFiles.Count);

            var token = _cts.Token;

            Log($"Starting auto-tagging of {imageFiles.Count} image(s)...");
            Log($"llama-cli : {llamaPath}");
            Log($"Model     : {modelPath}");
            Log($"mmproj    : {mmprojPath}");
            Log(string.Empty);

            try
            {
                bool onlyDbTags = OnlyDbTagsCheckBox.IsChecked == true;
                await RunAutoTagging(llamaPath, modelPath, mmprojPath, imageFiles, onlyDbTags, token);
            }
            catch (OperationCanceledException)
            {
                Log("\n[CANCELLED] Auto-tagging was cancelled.");
            }
            catch (Exception ex)
            {
                Log($"\n[ERROR] {ex.Message}");
            }
            finally
            {
                EndRun();
            }
        }

        private async void TestOllama_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                MessageBox.Show("A tagging run is already in progress.", "Busy",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var host = OllamaHostTextBox.Text.Trim();
            var model = OllamaModelTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("Please specify the Ollama host URL.", "Missing Setting",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(model))
            {
                MessageBox.Show("Please specify the Ollama model name.", "Missing Setting",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(OllamaNumCtxTextBox.Text.Trim(), out var numCtx) || numCtx <= 0)
            {
                MessageBox.Show("Please specify a valid context size (num_ctx), e.g. 65536.", "Missing Setting",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveOllamaSettings();

            if (!TryCollectImageFiles(out var imageFiles, includeTagged: true))
                return;

            BeginRun(imageFiles.Count);

            var token = _cts.Token;

            Log($"Starting Ollama test for {imageFiles.Count} image(s)...");
            Log($"Host  : {host}");
            Log($"Model : {model}");
            Log($"num_ctx: {numCtx}");
            Log(string.Empty);

            try
            {
                bool onlyDbTags = OnlyDbTagsCheckBox.IsChecked == true;
                int processed = 0;

                foreach (var imagePath in imageFiles)
                {
                    token.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(imagePath);
                    Log($"[{processed + 1}/{imageFiles.Count}] {fileName}");

                    var allTagNames = DatabaseManager.Instance.GetAllTagNamesForPrompt();
                    var tagListString = onlyDbTags && allTagNames.Any()
                        ? string.Join(", ", allTagNames)
                        : string.Empty;

                    if (onlyDbTags && !allTagNames.Any())
                        Log("  No tags in database — using default prompt.");

                    Log("  Sending request to Ollama...");

                    string output;
                    try
                    {
                        output = await InvokeOllama(host, model, imagePath, tagListString, numCtx, token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Log($"\n[ERROR] Ollama request failed — stopping.\n{ex.Message}");
                        throw;
                    }

                    if (string.IsNullOrWhiteSpace(output))
                        Log("  [EMPTY] No content in Ollama response.");
                    else
                        Log($"  Response:\n{output}");

                    Log(string.Empty);
                    UpdateProgress(++processed, imageFiles.Count);
                }

                Log($"[DONE] Ollama test complete. Processed {processed} image(s).");
            }
            catch (OperationCanceledException)
            {
                Log("\n[CANCELLED] Ollama test was cancelled.");
            }
            catch (Exception ex)
            {
                Log($"\n[ERROR] {ex.Message}");
            }
            finally
            {
                EndRun();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            CancelButton.IsEnabled = false;
            Log("\n[CANCELLING] Waiting for current image to complete...");
        }

        #endregion

        #region Core Auto-Tagging Logic

        private async Task RunAutoTagging(string llamaPath, string modelPath, string mmprojPath,
            List<string> imageFiles, bool onlyDbTags, CancellationToken token)
        {
            int processed = 0;
            int tagsAdded = 0;

            foreach (var imagePath in imageFiles)
            {
                token.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(imagePath);
                Log($"[{processed + 1}/{imageFiles.Count}] {fileName}");

                // Re-fetch all tags from DB before each image so newly added tags are included
                var allTagNames = DatabaseManager.Instance.GetAllTagNamesForPrompt();

                if (onlyDbTags && !allTagNames.Any())
                {
                    Log("  [SKIP] No tags in database to match against.");
                    UpdateProgress(++processed, imageFiles.Count);
                    continue;
                }

                var tagListString = onlyDbTags ? string.Join(", ", allTagNames) : string.Empty;

                // Run llama-cli
                string output;
                try
                {
                    output = await InvokeLlama(llamaPath, modelPath, mmprojPath, imagePath, tagListString, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log($"\n[ERROR] llama-cli failed — stopping.\n{ex.Message}");
                    throw;
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    Log("  [SKIP] Empty output from llama-cli.");
                    UpdateProgress(++processed, imageFiles.Count);
                    continue;
                }

                var preview = output.Length > 250 ? output[..250] + "..." : output;

                var parsedTokens = ParseTokens(output);


                List<string> matchedTags;
                if (onlyDbTags)
                {
                    var knownTagsSet = new HashSet<string>(allTagNames, StringComparer.OrdinalIgnoreCase);
                    matchedTags = parsedTokens
                        .Where(t => knownTagsSet.Contains(t))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (!matchedTags.Any())
                    {
                        Log("  No tags from the database matched the output.");
                        UpdateProgress(++processed, imageFiles.Count);
                        continue;
                    }
                }
                else
                {
                    matchedTags = parsedTokens
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (!matchedTags.Any())
                    {
                        Log("  No tags returned by LLM.");
                        UpdateProgress(++processed, imageFiles.Count);
                        continue;
                    }
                }

                Log($"  Tags: {string.Join(", ", matchedTags)}");

                // throw new InvalidOperationException($"llama-cli exited with code");

                // Determine which matched tags are new to this image
                var existingFileTags = new HashSet<string>(
                    DatabaseManager.Instance.GetTagsForFile(imagePath),
                    StringComparer.OrdinalIgnoreCase);

                var newTags = matchedTags.Where(t => !existingFileTags.Contains(t)).ToList();

                if (!newTags.Any())
                {
                    Log($"  Matched {matchedTags.Count} tag(s) — all already assigned.");
                }
                else
                {
                    foreach (var tag in newTags)
                    {
                        DatabaseManager.Instance.AddTagToFile(imagePath, tag);
                        tagsAdded++;
                    }
                    Log($"  Added {newTags.Count} tag(s): {string.Join(", ", newTags)}");
                }

                // Mark image as AI-processed regardless of whether new tags were added
                DatabaseManager.Instance.AddTagToFile(imagePath, AiTaggedUnchecked);

                UpdateProgress(++processed, imageFiles.Count);
            }

            Log($"\n[DONE] Processed {processed} image(s), added {tagsAdded} tag association(s) in total.");
        }

        private static (string sysPrompt, string userPrompt) BuildTaggingPrompts(string tagList)
        {
            var sysPrompt = "List sexual content tags for this image." +
                            "Respond with ONLY a comma-separated list of tags, nothing else. " +
                            "No sentences, no explanations, no preamble";

            var userPrompt = string.IsNullOrEmpty(tagList)
                ? BasicPromt
                : $"List tags for this image. Only use tags from this list: {tagList}";

            return (sysPrompt, userPrompt);
        }

        private static async Task<string> InvokeLlama(string llamaPath, string modelPath, string mmprojPath,
            string imagePath, string tagList, CancellationToken token)
        {
            var (sysPrompt, prompt) = BuildTaggingPrompts(tagList);

            var escapedSys = sysPrompt.Replace("\"", "\\\"");
            var escapedPrompt = prompt.Replace("\"", "\\\"");

            var psi = new ProcessStartInfo
            {
                FileName = llamaPath,
                Arguments = $"-m \"{modelPath}\" --mmproj \"{mmprojPath}\" --image \"{imagePath}\" -sys \"{escapedSys}\" -p \"{escapedPrompt}\" --single-turn --simple-io -ngl 999",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            // Allow cancellation to kill the child process
            using var reg = token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
            });

            var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
            var stderrTask = process.StandardError.ReadToEndAsync(token);

            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(token);

            token.ThrowIfCancellationRequested();

            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"llama-cli exited with code {process.ExitCode}." +
                    (string.IsNullOrWhiteSpace(stderr) ? string.Empty : $"\n{stderr}"));

            return stdout;
        }

        private static async Task<string> InvokeOllama(string host, string model, string imagePath,
            string tagList, int numCtx, CancellationToken token)
        {
            var (sysPrompt, userPrompt) = BuildTaggingPrompts(tagList);

            var imageBase64 = Convert.ToBase64String(
                await File.ReadAllBytesAsync(imagePath, token));

            var requestBody = new
            {
                model,
                stream = false,
                messages = new object[]
                {
                    new { role = "system", content = sysPrompt },
                    new { role = "user", content = userPrompt, images = new[] { imageBase64 } }
                },
                options = new { num_ctx = numCtx }
            };

            var url = host.TrimEnd('/') + "/api/chat";
            using var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            using var response = await OllamaHttp.PostAsync(url, content, token);
            var json = await response.Content.ReadAsStringAsync(token);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Ollama HTTP {(int)response.StatusCode}: {json}");

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }

        private static List<string> ParseTokens(string output)
        {
            var idx = output.IndexOf(BasicPromt, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                output = output[(idx + BasicPromt.Length)..];

            var bracketIdx = output.IndexOf('[');
            if (bracketIdx >= 0)
                output = output[..bracketIdx];

            return output
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().Trim('.', '!', '?', ';', ':'))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
        }

        #endregion

        #region UI Helpers

        private void UpdateProgress(int current, int total)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = current;
                ProgressLabel.Text = $"Processed {current} / {total}";
            });
        }

        private void Log(string message)
        {
            void append()
            {
                LogTextBox.AppendText(message + Environment.NewLine);
                LogTextBox.CaretIndex = LogTextBox.Text.Length;
                LogTextBox.ScrollToEnd();
            }

            if (Dispatcher.CheckAccess())
                append();
            else
                Dispatcher.Invoke(append);
        }

        #endregion

        #region Window Events

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                var result = MessageBox.Show(
                    "Auto-tagging is still running. Cancel it and close?",
                    "Confirm Close", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;
                _cts?.Cancel();
            }
            Close();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _cts?.Cancel();
        }

        #endregion
    }
}
