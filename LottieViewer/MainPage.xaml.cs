// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

//#define DebugDragDrop
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Toolkit.Uwp.UI.Lottie;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

namespace LottieViewer
{
    /// <summary>
    /// MainPage.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        int _playVersion;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        public MainPage()
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
        {
            InitializeComponent();

            // Connect the player's progress to the scrubber's progress.
            _scrubber.SetAnimatedCompositionObject(_stage.Player.ProgressObject);
        }

        // Avoid "async void" method. Not valid here because we handle all exceptions.
#pragma warning disable VSTHRD100
        async void SaveFile_Click(object sender, RoutedEventArgs e)
        {
#pragma warning restore VSTHRD100
            var diagnostics = _stage.Player.Diagnostics as LottieVisualDiagnostics;
            if (diagnostics == null)
            {
                return;
            }

            var filePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = diagnostics.SuggestedFileName,
            };

            // Dropdown of file types the user can save the file as
            filePicker.FileTypeChoices.Add("C#", new[] { ".cs" });
            filePicker.FileTypeChoices.Add("C++ CX", new[] { ".cpp" });
            filePicker.FileTypeChoices.Add("Lottie XML", new[] { ".xml" });

            // Note that the extension needs to be unique if we're going to
            // recognize the choice when the file is saved.
            //filePicker.FileTypeChoices.Add("WinComp XML", new[] { ".xml" });
            StorageFile pickedFile = null;
            try
            {
                pickedFile = await filePicker.PickSaveFileAsync();
            }
            catch
            {
                // Ignore exceptions from PickSaveFileAsync()
            }

            if (pickedFile == null)
            {
                // No source file chosen - give up.
                return;
            }

            var suggestedClassName = Path.GetFileNameWithoutExtension(pickedFile.Name);

            switch (pickedFile.FileType)
            {
                // If an unrecognized file type is specified, treat it as C#.
                default:
                case ".cs":
                    await FileIO.WriteTextAsync(pickedFile, diagnostics.GenerateCSharpCode());
                    break;
                case ".cpp":
                    await GenerateCxCodeAsync(diagnostics, suggestedClassName, pickedFile);
                    break;
                case ".xml":
                    await FileIO.WriteTextAsync(pickedFile, diagnostics.GenerateLottieXml());
                    break;
            }
        }

        async Task GenerateCxCodeAsync(LottieVisualDiagnostics diagnostics, string suggestedClassName, IStorageFile cppFile)
        {
            // Ask the user to pick a name for the .h file.
            var filePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = suggestedClassName,
            };

            // Dropdown of file types the user can save the file as
            filePicker.FileTypeChoices.Add("C++ CX header", new[] { ".h" });

            var hFile = await filePicker.PickSaveFileAsync();

            if (hFile == null)
            {
                // No header file chosen - give up.
                return;
            }

            // Ask the user to pick a name for the ICompositionSource.h file.
            var iCompositionSourceFilePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = "ICompositionSource.h",
            };

            // Dropdown of file types the user can save the file as
            iCompositionSourceFilePicker.FileTypeChoices.Add("ICompositionSource header", new[] { ".h" });
            var iCompositionSourceHeader = await iCompositionSourceFilePicker.PickSaveFileAsync();

            // Generate the .cpp and the .h text.
            diagnostics.GenerateCxCode(hFile.Name, out var cppText, out var hText);

            // Write the .cpp file.
            await FileIO.WriteTextAsync(cppFile, cppText);

            // Write the .h file if the user specified one.
            if (hFile != null)
            {
                await FileIO.WriteTextAsync(hFile, hText);
            }

            // Write the ICompositionSource.h file if the user specified it.
            if (iCompositionSourceHeader != null)
            {
                await FileIO.WriteLinesAsync(iCompositionSourceHeader, new[]
                {
                    "#pragma once",
                    "namespace Compositions",
                    "{",
                    "    public interface class ICompositionSource",
                    "    {",
                    "        virtual bool TryCreateAnimatedVisual(",
                    "        Windows::UI::Composition::Compositor^ compositor,",
                    "        Windows::UI::Composition::Visual^* rootVisual,",
                    "        Windows::Foundation::Numerics::float2* size,",
                    "        Windows::Foundation::TimeSpan* duration,",
                    "        Platform::Object^* diagnostics);",
                    "    };",
                    "}",
                    string.Empty,
                });
            }
        }

        // Avoid "async void" method. Not valid here because we handle all async exceptions.
#pragma warning disable VSTHRD100
        async void PickFile_Click(object sender, RoutedEventArgs e)
        {
#pragma warning restore VSTHRD100
            var playVersion = ++_playVersion;

            var filePicker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.ComputerFolder,
            };
            filePicker.FileTypeFilter.Add(".json");

            StorageFile file = null;
            try
            {
                file = await filePicker.PickSingleFileAsync();
            }
            catch
            {
                // Ignore PickSingleFileAsync exceptions so they don't crash the process.
            }

            if (file == null)
            {
                // Used declined to pick anything.
                return;
            }

            if (playVersion != _playVersion)
            {
                return;
            }

            // Reset the scrubber to the 0 position.
            _scrubber.Value = 0;

            // If we were stopped in manual play control, turn it back to automatic.
            if (!_playStopButton.IsChecked.Value)
            {
                _playStopButton.IsChecked = true;
            }

            _stage.DoDragDropped(file);
        }

        // Avoid "async void" method. Not valid here because we handle all async exceptions.
#pragma warning disable VSTHRD100
        async void LottieDragEnterHandler(object sender, DragEventArgs e)
        {
#pragma warning restore VSTHRD100
            DebugDragDrop("Drag enter");

            // Only accept files.
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                // Get a deferral to keep the drag operation alive until the async
                // methods have completed.
                var deferral = e.GetDeferral();
                try
                {
                    var items = await e.DataView.GetStorageItemsAsync();

                    var filteredItems = items.Where(IsJsonFile);

                    if (!filteredItems.Any() || filteredItems.Skip(1).Any())
                    {
                        DebugDragDrop("Drag enter - ignoring");
                        return;
                    }

                    // Exactly one item was selected.
                    e.AcceptedOperation = DataPackageOperation.Copy;
                    e.DragUIOverride.Caption = "Drop to view Lottie.";
                }
                catch
                {
                    // Ignore async exception so they don't crash the process.
                }
                finally
                {
                    DebugDragDrop("Completing drag deferral");
                    deferral.Complete();
                }

                DebugDragDrop("Doing drag enter");
                _stage.DoDragEnter();
            }
        }

        // Avoid "async void" method. Not valid here because we handle all async exceptions.
#pragma warning disable VSTHRD100

        // Called when an item is dropped.
        async void LottieDropHandler(object sender, DragEventArgs e)
        {
#pragma warning restore VSTHRD100
            DebugDragDrop("Dropping");
            var playVersion = ++_playVersion;

            IStorageItem item = null;
            try
            {
                item = (await e.DataView.GetStorageItemsAsync()).Single();
            }
            catch
            {
                // Ignore GetStorageItemsAsync exceptions so they don't crash the process.
            }

            if (playVersion != _playVersion)
            {
                DebugDragDrop("Ignoring drop");
                return;
            }

            // Reset the scrubber to the 0 position.
            _scrubber.Value = 0;

            // If we were stopped in manual play control, turn it back to automatic.
            if (!_playStopButton.IsChecked.Value)
            {
                _playStopButton.IsChecked = true;
            }

            DebugDragDrop("Doing drop");
            _stage.DoDragDropped((StorageFile)item);
        }

        void LottieDragLeaveHandler(object sender, DragEventArgs e)
        {
            _stage.DoDragLeave();
        }

        [Conditional("DebugDragDrop")]
        static void DebugDragDrop(string text) => Debug.WriteLine(text);

        static bool IsJsonFile(IStorageItem item) => item.IsOfType(StorageItemTypes.File) && item.Name.EndsWith(".json", StringComparison.InvariantCultureIgnoreCase);

        bool _ignoreScrubberValueChanges;

        void ProgressSliderChanged(object sender, ScrubberValueChangedEventArgs e)
        {
            if (!_ignoreScrubberValueChanges)
            {
                _playStopButton.IsChecked = false;
                _stage.Player.SetProgress(e.NewValue);
            }
        }

        // Avoid "async void" method. Not valid here because we handle all async exceptions.
#pragma warning disable VSTHRD100
#pragma warning disable SA1300 // Element should begin with upper-case letter
        async void _playControl_Toggled(object sender, RoutedEventArgs e)
#pragma warning restore SA1300 // Element should begin with upper-case letter
        {
#pragma warning restore VSTHRD100
            // If no Lottie is loaded, do nothing.
            if (!_stage.Player.IsAnimatedVisualLoaded)
            {
                return;
            }

            // Otherwise, if we toggled on, we're stopped in manual mode: set the progress.
            //            If we toggled off, we're in auto mode, start playing.
            if (!_playStopButton.IsChecked.Value)
            {
                _stage.Player.SetProgress(_scrubber.Value);
            }
            else
            {
                _ignoreScrubberValueChanges = true;
                _scrubber.Value = 0;
                _ignoreScrubberValueChanges = false;

                // If we were stopped in manual play control, turn it back to automatic.
                if (!_playStopButton.IsChecked.Value)
                {
                    _playStopButton.IsChecked = true;
                }

                try
                {
                    await _stage.Player.PlayAsync(0, 1, looped: true);
                }
                catch
                {
                    // Ignore PlayAsync exceptions so they don't crash the process.
                }
            }
        }

        void CopyIssuesToClipboard(object sender, RoutedEventArgs e)
        {
            var issues = _stage.PlayerIssues;
            var dataPackage = new DataPackage();
            dataPackage.RequestedOperation = DataPackageOperation.Copy;
            dataPackage.SetText(string.Join("\r\n", issues.Select(iss => iss.ToString())));
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
        }
    }

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public sealed class VisiblityConverter : IValueConverter
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
#pragma warning restore SA1402 // File may only contain a single type
    {
        object IValueConverter.Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool boolValue)
            {
                if ((string)parameter == "not")
                {
                    boolValue = !boolValue;
                }

                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }

            return null;
        }

        object IValueConverter.ConvertBack(object value, Type targetType, object parameter, string language)
        {
            // Only support one way binding.
            throw new NotImplementedException();
        }
    }

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public sealed class FloatFormatter : IValueConverter
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
#pragma warning restore SA1402 // File may only contain a single type
    {
        object IValueConverter.Convert(object value, Type targetType, object parameter, string language)
        {
            return ((double)value).ToString("0.#");
        }

        object IValueConverter.ConvertBack(object value, Type targetType, object parameter, string language)
        {
            // Only support one way binding.
            throw new NotImplementedException();
        }
    }

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public sealed class LottieVisualDiagnosticsFormatter : IValueConverter
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
#pragma warning restore SA1402 // File may only contain a single type
    {
        static string MSecs(TimeSpan timeSpan) => $"{timeSpan.TotalMilliseconds.ToString("#,##0.0")} mSecs";

        object IValueConverter.Convert(object value, Type targetType, object parameter, string language)
        {
            if (parameter as string == "CollapsedIfNull" && targetType == typeof(Visibility))
            {
                return value == null ? Visibility.Collapsed : Visibility.Visible;
            }

            var diagnostics = value as LottieVisualDiagnostics;

            switch (parameter as string)
            {
                case "Properties":
                    if (diagnostics == null) { return null; }
                    return DiagnosticsToProperties(diagnostics).ToArray();
                case "Issues":
                    {
                        if (diagnostics == null) { return null; }
                        var allIssues = diagnostics.JsonParsingIssues.Select(iss => iss.Description).Concat(diagnostics.TranslationIssues.Select(iss => iss.Description));
                        if (targetType == typeof(Visibility))
                        {
                            return allIssues.Any() ? Visibility.Visible : Visibility.Collapsed;
                        }
                        else
                        {
                            return allIssues.OrderBy(a => a);
                        }
                    }

                case "ParsingIssues":
                    if (diagnostics == null) { return null; }
                    if (targetType == typeof(Visibility))
                    {
                        return diagnostics.JsonParsingIssues.Any() ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else
                    {
                        return diagnostics.JsonParsingIssues.OrderBy(a => a);
                    }

                case "TranslationIssues":
                    if (diagnostics == null) { return null; }
                    if (targetType == typeof(Visibility))
                    {
                        return diagnostics.TranslationIssues.Any() ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else
                    {
                        return diagnostics.TranslationIssues.OrderBy(a => a);
                    }

                case "VisibleIfIssues":
                    if (diagnostics == null)
                    {
                        return Visibility.Collapsed;
                    }

                    return diagnostics.JsonParsingIssues.Any() || diagnostics.TranslationIssues.Any() ? Visibility.Visible : Visibility.Collapsed;
                default:
                    break;
            }

            return null;
        }

        IEnumerable<Tuple<string, string>> DiagnosticsToProperties(LottieVisualDiagnostics diagnostics)
        {
            yield return Tuple.Create("File name", diagnostics.FileName);
            yield return Tuple.Create("Duration", $"{diagnostics.Duration.TotalSeconds.ToString("#,##0.0##")} secs");
            var aspectRatio = FloatToRatio(diagnostics.LottieWidth / diagnostics.LottieHeight);
            yield return Tuple.Create("Aspect ratio", $"{aspectRatio.Item1.ToString("0.###")}:{aspectRatio.Item2.ToString("0.###")}");
            yield return Tuple.Create("Size", $"{diagnostics.LottieWidth} x {diagnostics.LottieHeight}");

            //yield return Tuple.Create("Version", diagnostics.LottieVersion);
            //yield return Tuple.Create("Read", MSecs(diagnostics.ReadTime));
            //yield return Tuple.Create("Parse", MSecs(diagnostics.ParseTime));
            //yield return Tuple.Create("Validation", MSecs(diagnostics.ValidationTime));
            //yield return Tuple.Create("Translation", MSecs(diagnostics.TranslationTime));
            //yield return Tuple.Create("Instantiation", MSecs(diagnostics.InstantiationTime));
            foreach (var marker in diagnostics.Markers)
            {
                yield return Tuple.Create("Marker", $"{marker.Key}: {marker.Value.ToString("0.0###")}");
            }
        }

        object IValueConverter.ConvertBack(object value, Type targetType, object parameter, string language)
        {
            // Only support one way binding.
            throw new NotImplementedException();
        }

        // Returns a pleasantly simplified ratio for the given value.
        static (double, double) FloatToRatio(double value)
        {
            const int maxRatioProduct = 200;
            var candidateN = 1.0;
            var candidateD = Math.Round(1 / value);
            var error = Math.Abs(value - (candidateN / candidateD));

            for (double n = candidateN, d = candidateD; n * d <= maxRatioProduct && error != 0;)
            {
                if (value > n / d)
                {
                    n++;
                }
                else
                {
                    d++;
                }

                var newError = Math.Abs(value - (n / d));
                if (newError < error)
                {
                    error = newError;
                    candidateN = n;
                    candidateD = d;
                }
            }

            // If we gave up because the numerator or denominator got too big then
            // the number is an approximation that requires some decimal places.
            // Get the real ratio by adjusting the denominator or numerator - whichever
            // requires the smallest adjustment.
            if (error != 0)
            {
                if (value > candidateN / candidateD)
                {
                    candidateN = candidateD * value;
                }
                else
                {
                    candidateD = candidateN / value;
                }
            }

            return (candidateN, candidateD);
        }
    }
}
