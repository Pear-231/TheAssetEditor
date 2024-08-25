using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Editors.Audio.AudioEditor.ViewModels;
using Editors.Audio.Storage;
using Editors.Audio.Utility;
using static Editors.Audio.AudioEditor.AudioEditorHelpers;
using static Editors.Audio.AudioEditor.DataGridHelpers;

namespace Editors.Audio.AudioEditor
{
    public class DialogueEventDataGrid
    {
        public DialogueEventDataGrid()
        {
        }

        public static void ConfigureDataGrid(AudioEditorViewModel viewModel, IAudioRepository audioRepository, string dataGridName, ObservableCollection<Dictionary<string, object>> dataGridData)
        {
            var selectedAudioProjectEvent = viewModel.SelectedAudioProjectEvent;

            var dataGrid = GetDataGrid(dataGridName);
            dataGrid.CanUserAddRows = false; // Setting this bastard to false ensures that data won't go missing from the last row when a new row is added. Wtf WPF.
            dataGrid.ItemsSource = dataGridData;
            dataGrid.Columns.Clear();

            var stateGroups = audioRepository.DialogueEventsWithStateGroups[selectedAudioProjectEvent];
            var stateGroupsWithQualifiers = AudioProject.DialogueEventsWithStateGroupsWithQualifiers[selectedAudioProjectEvent];

            var stateGroupsCount = stateGroups.Count() + 1;
            var columnWidth = stateGroupsCount > 0 ? 1.0 / stateGroupsCount : 1.0;

            foreach (var kvp in stateGroupsWithQualifiers)
            {
                var stateGroupWithQualifier = kvp.Key;

                // Column for State Group:
                var stateGroupColumn = new DataGridTemplateColumn
                {
                    Header = AddExtraUnderscoresToString(stateGroupWithQualifier),
                    Width = new DataGridLength(columnWidth, DataGridLengthUnitType.Star),
                    IsReadOnly = true
                };

                var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));

                textBlockFactory.SetBinding(TextBlock.TextProperty, new Binding($"[{AddExtraUnderscoresToString(stateGroupWithQualifier)}]"));
                textBlockFactory.SetValue(TextBlock.PaddingProperty, new Thickness(5, 2.5, 2.5, 5));

                var cellTemplate = new DataTemplate();

                cellTemplate.VisualTree = textBlockFactory;

                stateGroupColumn.CellTemplate = cellTemplate;

                dataGrid.Columns.Add(stateGroupColumn);
            }

            // Column for Audio files TextBox with Tooltip:
            var soundsTextColumn = new DataGridTemplateColumn
            {
                Header = "Audio Files",
                Width = new DataGridLength(columnWidth, DataGridLengthUnitType.Star),
                IsReadOnly = true
            };

            var soundsCellTemplate = new DataTemplate();
            var soundsTextBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
            soundsTextBlockFactory.SetBinding(TextBlock.TextProperty, new Binding("[AudioFilesDisplay]"));

            // Create and set the tooltip binding:
            var tooltipBinding = new Binding("[AudioFiles]")
            {
                Mode = BindingMode.OneWay,
                Converter = new ConvertToolTipCollectionToString()
            };

            soundsTextBlockFactory.SetBinding(FrameworkElement.ToolTipProperty, tooltipBinding);
            soundsTextBlockFactory.SetValue(TextBlock.PaddingProperty, new Thickness(5, 2.5, 2.5, 5));

            soundsCellTemplate.VisualTree = soundsTextBlockFactory;
            soundsTextColumn.CellTemplate = soundsCellTemplate;

            dataGrid.Columns.Add(soundsTextColumn);
        }

        public static void ConfigureDataGridBuilder(AudioEditorViewModel viewModel, IAudioRepository audioRepository, bool showCustomStatesOnly, string dataGridName, ObservableCollection<Dictionary<string, object>> dataGridBuilderData)
        {
            var selectedAudioProjectEvent = viewModel.SelectedAudioProjectEvent;

            var dataGrid = GetDataGrid(dataGridName);
            dataGrid.CanUserAddRows = false; // Setting this bastard to false ensures that data won't go missing from the last row when a new row is added. Wtf WPF.
            dataGrid.ItemsSource = dataGridBuilderData;
            dataGrid.Columns.Clear();

            var stateGroups = audioRepository.DialogueEventsWithStateGroups[selectedAudioProjectEvent];
            var stateGroupsWithQualifiers = AudioProject.DialogueEventsWithStateGroupsWithQualifiers[selectedAudioProjectEvent];
            var stateGroupsWithCustomStates = AudioProject.AudioProjectInstance.StateGroupsWithCustomStates;

            var stateGroupsCount = stateGroups.Count() + 1;
            var columnWidth = stateGroupsCount > 0 ? 1.0 / stateGroupsCount : 1.0;

            foreach (var kvp in stateGroupsWithQualifiers)
            {
                var stateGroupWithQualifier = kvp.Key;
                var stateGroup = kvp.Value;

                var states = new List<string>();
                var customStates = new List<string>();

                var vanillaStates = audioRepository.StateGroupsWithStates[stateGroup];

                if (stateGroupsWithCustomStates != null && stateGroupsWithCustomStates.Count() > 0)
                {
                    if (stateGroup == "VO_Actor" || stateGroup == "VO_Culture" || stateGroup == "VO_Battle_Selection" || stateGroup == "VO_Battle_Special_Ability" || stateGroup == "VO_Faction_Leader")
                        customStates = stateGroupsWithCustomStates[stateGroup];
                }

                if (showCustomStatesOnly && (stateGroup == "VO_Actor" || stateGroup == "VO_Culture" || stateGroup == "VO_Battle_Selection" || stateGroup == "VO_Battle_Special_Ability" || stateGroup == "VO_Faction_Leader"))
                {
                    states.Add("Any"); // Still needs an Any State in addition to custom States.
                    states.AddRange(customStates);
                }

                else
                {
                    if (stateGroup == "VO_Actor" || stateGroup == "VO_Culture" || stateGroup == "VO_Battle_Selection" || stateGroup == "VO_Battle_Special_Ability" || stateGroup == "VO_Faction_Leader")
                        states.AddRange(customStates);

                    states.AddRange(vanillaStates);
                }

                // Column for State Group:
                var column = new DataGridTemplateColumn
                {
                    Header = AddExtraUnderscoresToString(stateGroupWithQualifier),
                    CellTemplate = CreateStatesComboBoxTemplate(states, stateGroupWithQualifier, showCustomStatesOnly),
                    Width = new DataGridLength(columnWidth, DataGridLengthUnitType.Star),
                };

                dataGrid.Columns.Add(column);
            }

            // Column for Audio files TextBox:
            var soundsTextBoxColumn = new DataGridTemplateColumn
            {
                Header = "Audio Files",
                CellTemplate = CreateSoundsTextBoxTemplate(),
                Width = new DataGridLength(columnWidth, DataGridLengthUnitType.Star),
            };

            dataGrid.Columns.Add(soundsTextBoxColumn);

            // Column for Audio files '...' button:
            var soundsButtonColumn = new DataGridTemplateColumn
            {
                CellTemplate = CreateSoundsButtonTemplate(audioRepository),
                Width = 30.0,
                CanUserResize = false
            };

            dataGrid.Columns.Add(soundsButtonColumn);
        }

        public static DataTemplate CreateStatesComboBoxTemplate(List<string> states, string stateGroupWithQualifier, bool showCustomStatesOnly)
        {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(ComboBox));

            var binding = new Binding($"[{AddExtraUnderscoresToString(stateGroupWithQualifier)}]")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };

            factory.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, args) =>
            {
                if (sender is ComboBox comboBox)
                {
                    comboBox.ItemsSource = states;

                    // Ensure that the text box is not highlighted after selection
                    if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is TextBox textBox)
                    {
                        textBox.TextChanged += (s, e) =>
                        {
                            var filterText = textBox.Text;
                            var filteredItems = states.Where(item => item.Contains(filterText, StringComparison.OrdinalIgnoreCase)).ToList();

                            comboBox.ItemsSource = filteredItems;
                            comboBox.IsDropDownOpen = true; // Keep the drop-down open to show filtered results.
                        };

                        // Handle LostFocus event to ensure final text is genuinely a State and warn the user if not.
                        textBox.LostFocus += (s, e) =>
                        {
                            var finalText = textBox.Text;

                            if (!string.IsNullOrWhiteSpace(finalText) && !states.Contains(finalText))
                            {
                                MessageBox.Show("Invalid State. Select a State from the list.", "Invalid State", MessageBoxButton.OK, MessageBoxImage.Warning);
                                textBox.Text = string.Empty;
                                comboBox.SelectedItem = null;
                            }
                        };

                        // Handle SelectionChanged event to clear text selection after item is selected
                        comboBox.SelectionChanged += (s, e) =>
                        {
                            // Ensure the ComboBox's TextBox does not highlight text
                            if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is TextBox selectedTextBox)
                            {
                                selectedTextBox.Dispatcher.InvokeAsync(() =>
                                {
                                    selectedTextBox.Select(0, 0); // Remove selection by setting selection start and length to 0
                                    selectedTextBox.CaretIndex = 0; // Move the caret to the start position
                                });
                            }
                        };
                    }
                }
            }));

            factory.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, binding);
            factory.SetValue(ItemsControl.IsTextSearchEnabledProperty, true);
            factory.SetValue(ComboBox.IsEditableProperty, true);
            factory.SetValue(ItemsControl.ItemsSourceProperty, states);

            template.VisualTree = factory;

            return template;
        }



        public static DataTemplate CreateSoundsTextBoxTemplate()
        {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(TextBox));

            var binding = new Binding("[AudioFilesDisplay]")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };

            var tooltipBinding = new Binding("[AudioFiles]")
            {
                Mode = BindingMode.TwoWay,
                Converter = new ConvertToolTipCollectionToString()
            };

            factory.SetValue(FrameworkElement.NameProperty, "AudioFilesDisplay");
            factory.SetBinding(TextBox.TextProperty, binding);
            factory.SetBinding(FrameworkElement.ToolTipProperty, tooltipBinding);
            factory.SetValue(System.Windows.Controls.Primitives.TextBoxBase.IsReadOnlyProperty, true);

            template.VisualTree = factory;

            return template;
        }

        public static DataTemplate CreateSoundsButtonTemplate(IAudioRepository audioRepository)
        {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(Button));

            // Handle button click event
            factory.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((sender, e) =>
            {
                var button = sender as Button;
                var dataGridRow = FindVisualParent<DataGridRow>(button);

                if (dataGridRow != null)
                {
                    var textBox = FindVisualChild<TextBox>(dataGridRow, "AudioFilesDisplay");

                    if (textBox != null)
                    {
                        var rowDataContext = dataGridRow.DataContext;

                        if (rowDataContext is Dictionary<string, object> dataGridRowContext)
                            AudioEditorViewModel.AddAudioFiles(dataGridRowContext, textBox);
                    }
                }
            }));

            factory.SetValue(ContentControl.ContentProperty, "...");
            factory.SetValue(FrameworkElement.ToolTipProperty, "Browse wav files");

            template.VisualTree = factory;

            return template;
        }
    }
}
