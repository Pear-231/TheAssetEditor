using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Editors.Audio.AudioEditor
{
    public static class AudioEditorHelpers
    {
        // Apparently WPF doesn't_like_underscores so double them up in order for them to be displayed in the UI.
        public static string AddExtraUnderscoresToString(string wtfWPF)
        {
            return wtfWPF.Replace("_", "__");
        }

        public static string RemoveExtraUnderscoresFromString(string wtfWPF)
        {
            return wtfWPF.Replace("__", "_");
        }

        public static DecisionNode GetMatchingDecisionNode(StatePath comparisonStatePath, DialogueEvent selectedDialogueEvent)
        {
            foreach (var decisionNode in selectedDialogueEvent.DecisionTree)
            {
                var stateGroups = decisionNode.StatePath.Nodes.Select(node => node.StateGroup.Name).ToList();
                var comparisonStateGroups = comparisonStatePath.Nodes.Select(node => node.StateGroup.Name).ToList();
                if (!stateGroups.SequenceEqual(comparisonStateGroups))
                    return null;

                var states = decisionNode.StatePath.Nodes.Select(node => node.State).ToList();
                var comparisonStates = comparisonStatePath.Nodes.Select(node => node.State).ToList();
                if (states.SequenceEqual(comparisonStates))
                    return decisionNode;
            }

            return null;
        }

        public static string GetStateGroupFromStateGroupWithQualifier(string dialogueEvent, string stateGroupWithQualifier, Dictionary<string, Dictionary<string, string>> dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository)
        {
            if (dialogueEventsWithStateGroupsWithQualifiersAndStateGroupsRepository.TryGetValue(dialogueEvent, out var stateGroupDictionary))
            {
                if (stateGroupDictionary.TryGetValue(stateGroupWithQualifier, out var stateGroup))
                    return stateGroup;
            }
            return null;
        }

        public static void AddAudioFilesToAudioProjectEditorSingleRowDataGrid(Dictionary<string, object> dataGridRow, TextBox textBox)
        {
            var dialog = new OpenFileDialog()
            {
                Multiselect = true,
                Filter = "WAV files (*.wav)|*.wav"
            };

            if (dialog.ShowDialog() == true)
            {
                var filePaths = dialog.FileNames;
                var fileNames = filePaths.Select(Path.GetFileName);
                var fileNamesString = string.Join(", ", fileNames);
                var filePathsString = string.Join(", ", filePaths.Select(filePath => $"\"{filePath}\""));

                textBox.Text = fileNamesString;
                textBox.ToolTip = filePathsString;

                var audioFiles = new List<string>(filePaths);
                dataGridRow["AudioFiles"] = audioFiles;
                dataGridRow["AudioFilesDisplay"] = fileNamesString;
            }
        }

        public static DataGrid GetDataGrid(string dataGridTag)
        {
            var mainWindow = Application.Current.MainWindow;
            return FindVisualChild<DataGrid>(mainWindow, dataGridTag);
        }

        public static void ClearDataGrid(ObservableCollection<Dictionary<string, object>> audioProjectEditorFullDataGrid)
        {
            audioProjectEditorFullDataGrid.Clear();
        }

        public static T FindVisualChild<T>(DependencyObject parent, string identifier) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild && child is FrameworkElement element)
                {
                    // Check both Name and Tag because DataGrids use Tag as Name can't be set via a binding for some reason...
                    if (element.Name == identifier || element.Tag?.ToString() == identifier)
                        return typedChild;
                }

                var foundChild = FindVisualChild<T>(child, identifier);
                if (foundChild != null)
                    return foundChild;
            }
            return null;
        }

        public static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while ((child = VisualTreeHelper.GetParent(child)) != null)
            {
                if (child is T parent)
                    return parent;
            }
            return null;
        }
    }
}
