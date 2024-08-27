using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Editors.Audio.AudioEditor.ViewModels;
using Editors.Audio.Storage;
using static Editors.Audio.AudioEditor.AudioEditorHelpers;
using static Editors.Audio.AudioEditor.AudioEditorView;
using static Editors.Audio.AudioEditor.DataGridHelpers;

namespace Editors.Audio.AudioEditor
{
    public class DataGridConfiguration
    {
        public static void ConfigureStatesAudioProjectDataGrid(AudioEditorViewModel viewModel, string dataGridName, ObservableCollection<Dictionary<string, object>> dataGridBuilder)
        {
            var dataGrid = GetDataGrid(dataGridName);
            dataGrid.CanUserAddRows = false; // Setting this bastard to false ensures that data won't go missing from the last row when a new row is added. Wtf WPF.
            dataGrid.ItemsSource = dataGridBuilder;
            dataGrid.Columns.Clear();

            // Column for remove row button.
            var removeButtonColumn = new DataGridTemplateColumn
            {
                CellTemplate = CreateRemoveRowButtonTemplate(viewModel),
                Width = 20,
                CanUserResize = false
            };

            dataGrid.Columns.Add(removeButtonColumn);

            var stateGroups = StatesProjectData.ModdedStateGroups;
            var stateGroupsCount = stateGroups.Count + 3;
            var columnWidth = stateGroupsCount > 0 ? 1.0 / stateGroupsCount : 1.0;

            foreach (var stateGroup in stateGroups)
            {
                var stateGroupWithExtraUnderscores = AddExtraUnderscoresToString(stateGroup);

                // Column for State Group.
                var stateGroupColumn = new DataGridTemplateColumn
                {
                    Header = stateGroupWithExtraUnderscores,
                    Width = new DataGridLength(columnWidth, DataGridLengthUnitType.Star)
                };

                var textBoxFactory = new FrameworkElementFactory(typeof(TextBox));

                textBoxFactory.SetBinding(TextBox.TextProperty, new Binding($"[{stateGroupWithExtraUnderscores}]")
                {
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    Mode = BindingMode.TwoWay
                });

                textBoxFactory.SetValue(TextBox.PaddingProperty, new Thickness(5, 2.5, 2.5, 5));

                var cellTemplate = new DataTemplate
                {
                    VisualTree = textBoxFactory
                };

                stateGroupColumn.CellTemplate = cellTemplate;
                stateGroupColumn.CellEditingTemplate = cellTemplate;

                dataGrid.Columns.Add(stateGroupColumn);
            }
        }

        public static void ConfigureAudioProjectDataGrid(AudioEditorViewModel viewModel, IAudioRepository audioRepository, string dataGridName, ObservableCollection<Dictionary<string, object>> dataGridData)
        {
            var selectedAudioProjectEvent = viewModel.SelectedAudioProjectEvent;

            var dataGrid = GetDataGrid(dataGridName);
            dataGrid.CanUserAddRows = false; // Setting this bastard to false ensures that data won't go missing from the last row when a new row is added. Wtf WPF.
            dataGrid.ItemsSource = dataGridData;
            dataGrid.Columns.Clear();

            // Column for remove row button.
            var removeButtonColumn = new DataGridTemplateColumn
            {
                CellTemplate = CreateRemoveRowButtonTemplate(viewModel),
                Width = 20,
                CanUserResize = false
            };

            dataGrid.Columns.Add(removeButtonColumn);

            var stateGroups = audioRepository.DialogueEventsWithStateGroups[selectedAudioProjectEvent];
            var stateGroupsWithQualifiers = AudioProject.DialogueEventsWithStateGroupsWithQualifiers[selectedAudioProjectEvent];

            var stateGroupsCount = stateGroups.Count + 1;
            var columnWidth = stateGroupsCount > 0 ? 1.0 / stateGroupsCount : 1.0;

            foreach (var kvp in stateGroupsWithQualifiers)
            {
                var stateGroupWithQualifier = kvp.Key;
                var stateGroupWithQualifierWithExtraUnderscores = AddExtraUnderscoresToString(stateGroupWithQualifier);

                // Column for State Group.
                var stateGroupColumn = new DataGridTemplateColumn
                {
                    Header = stateGroupWithQualifierWithExtraUnderscores,
                    Width = new DataGridLength(columnWidth, DataGridLengthUnitType.Star),
                    IsReadOnly = true
                };

                var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));

                textBlockFactory.SetBinding(TextBlock.TextProperty, new Binding($"[{stateGroupWithQualifierWithExtraUnderscores}]"));
                textBlockFactory.SetValue(TextBlock.PaddingProperty, new Thickness(5, 2.5, 2.5, 5));

                var cellTemplate = new DataTemplate();

                cellTemplate.VisualTree = textBlockFactory;

                stateGroupColumn.CellTemplate = cellTemplate;

                dataGrid.Columns.Add(stateGroupColumn);
            }

            // Column for Audio files TextBox with Tooltip.
            var soundsTextColumn = new DataGridTemplateColumn
            {
                Header = "Audio Files",
                Width = new DataGridLength(columnWidth, DataGridLengthUnitType.Star),
                IsReadOnly = true
            };

            var soundsCellTemplate = new DataTemplate();
            var soundsTextBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
            soundsTextBlockFactory.SetBinding(TextBlock.TextProperty, new Binding("[AudioFilesDisplay]"));

            // Create and set the tooltip binding.
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

        public static void ConfigureAudioProjectDataGridBuilder(AudioEditorViewModel viewModel, IAudioRepository audioRepository, bool showModdedStates, string dataGridName, ObservableCollection<Dictionary<string, object>> dataGridBuilderData)
        {
            var selectedAudioProjectEvent = viewModel.SelectedAudioProjectEvent;

            if (string.IsNullOrEmpty(selectedAudioProjectEvent))
                return;

            var dataGrid = GetDataGrid(dataGridName);
            dataGrid.CanUserAddRows = false; // Setting this bastard to false ensures that data won't go missing from the last row when a new row is added. Wtf WPF.
            dataGrid.ItemsSource = dataGridBuilderData;
            dataGrid.Columns.Clear();

            var stateGroups = audioRepository.DialogueEventsWithStateGroups[selectedAudioProjectEvent];
            var stateGroupsWithQualifiers = AudioProject.DialogueEventsWithStateGroupsWithQualifiers[selectedAudioProjectEvent];
            var stateGroupsWithCustomStates = AudioProject.AudioProjectInstance.StateGroupsWithCustomStates;

            var stateGroupsCount = stateGroups.Count + 1;
            var columnWidth = stateGroupsCount > 0 ? 1.0 / stateGroupsCount : 1.0;

            foreach (var kvp in stateGroupsWithQualifiers)
            {
                var stateGroupWithQualifier = kvp.Key;
                var stateGroupWithQualifierWithExtraUnderscores = AddExtraUnderscoresToString(stateGroupWithQualifier);
                var stateGroup = kvp.Value;

                var states = new List<string>();
                var customStates = new List<string>();

                var vanillaStates = audioRepository.StateGroupsWithStates[stateGroup];

                if (stateGroupsWithCustomStates != null && stateGroupsWithCustomStates.Count > 0)
                {
                    if (stateGroup == "VO_Actor" || stateGroup == "VO_Culture" || stateGroup == "VO_Battle_Selection" || stateGroup == "VO_Battle_Special_Ability" || stateGroup == "VO_Faction_Leader")
                        customStates = stateGroupsWithCustomStates[stateGroup];
                }

                if (showModdedStates && (stateGroup == "VO_Actor" || stateGroup == "VO_Culture" || stateGroup == "VO_Battle_Selection" || stateGroup == "VO_Battle_Special_Ability" || stateGroup == "VO_Faction_Leader"))
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

                // Column for State Group.
                var column = new DataGridTemplateColumn
                {
                    Header = stateGroupWithQualifierWithExtraUnderscores,
                    CellTemplate = CreateStatesComboBoxTemplate(states, stateGroupWithQualifierWithExtraUnderscores, showModdedStates),
                    Width = new DataGridLength(columnWidth, DataGridLengthUnitType.Star),
                };

                dataGrid.Columns.Add(column);
            }

            // Column for Audio files TextBox.
            var soundsTextBoxColumn = new DataGridTemplateColumn
            {
                Header = "Audio Files",
                CellTemplate = CreateSoundsTextBoxTemplate(),
                Width = new DataGridLength(columnWidth, DataGridLengthUnitType.Star),
            };

            dataGrid.Columns.Add(soundsTextBoxColumn);

            // Column for Audio files '...' button.
            var soundsButtonColumn = new DataGridTemplateColumn
            {
                CellTemplate = CreateSoundsButtonTemplate(viewModel),
                Width = 30.0,
                CanUserResize = false
            };

            dataGrid.Columns.Add(soundsButtonColumn);
        }

        public static DataTemplate CreateStatesComboBoxTemplate(List<string> states, string stateGroupWithQualifierWithExtraUnderscores, bool showCustomStatesOnly)
        {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(ComboBox));

            // Convert the list of states to ObservableCollection for better binding support
            var observableStates = new ObservableCollection<string>(states);
            var collectionView = CollectionViewSource.GetDefaultView(observableStates);

            // Apply initial filtering to the CollectionView
            collectionView.Filter = item =>
            {
                if (item is string state)
                {
                    // Only show states that match the filtering criteria
                    return !showCustomStatesOnly || state.StartsWith("Custom", StringComparison.OrdinalIgnoreCase);
                }
                return false;
            };

            var binding = new Binding($"[{stateGroupWithQualifierWithExtraUnderscores}]")
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            };

            factory.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, args) =>
            {
                if (sender is ComboBox comboBox)
                {
                    // Set the ItemsSource to the ICollectionView
                    comboBox.ItemsSource = collectionView;

                    if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is TextBox textBox)
                    {
                        var debounceTimer = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(200),
                            IsEnabled = false
                        };

                        var lastFilterText = string.Empty;

                        textBox.TextChanged += (s, e) =>
                        {
                            lastFilterText = textBox.Text;
                            debounceTimer.Stop();
                            debounceTimer.Start();
                        };

                        debounceTimer.Tick += (s, e) =>
                        {
                            debounceTimer.Stop();
                            // Ensure UI updates are performed on the UI thread
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                collectionView.Filter = item =>
                                {
                                    if (item is string state)
                                    {
                                        return state.Contains(lastFilterText, StringComparison.OrdinalIgnoreCase);
                                    }
                                    return false;
                                };
                                comboBox.IsDropDownOpen = true;
                            });
                        };

                        textBox.LostFocus += (s, e) =>
                        {
                            var finalText = textBox.Text;

                            if (!string.IsNullOrWhiteSpace(finalText) && !states.Contains(finalText))
                            {
                                textBox.Text = string.Empty;
                                comboBox.SelectedItem = null;
                            }
                        };

                        comboBox.SelectionChanged += (s, e) =>
                        {
                            // Ensure the ComboBox's TextBox does not highlight text
                            if (comboBox.Template.FindName("PART_EditableTextBox", comboBox) is TextBox selectedTextBox)
                            {
                                selectedTextBox.Dispatcher.Invoke(() =>
                                {
                                    selectedTextBox.Select(0, 0); // Remove selection
                                    selectedTextBox.CaretIndex = selectedTextBox.Text.Length; // Move caret to the end
                                });
                            }
                        };
                    }
                }
            }));

            factory.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, binding);
            factory.SetValue(ItemsControl.IsTextSearchEnabledProperty, true);
            factory.SetValue(ComboBox.IsEditableProperty, true);

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

        public static DataTemplate CreateSoundsButtonTemplate(AudioEditorViewModel viewModel)
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

        public static DataTemplate CreateRemoveRowButtonTemplate(AudioEditorViewModel viewModel)
        {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(Button));
            factory.SetValue(ContentControl.ContentProperty, "✖");
            factory.SetValue(Control.FontFamilyProperty, new FontFamily("Segoe UI Symbol")); // This font supports the character.
            factory.SetValue(FrameworkElement.ToolTipProperty, "PLACEHOLDER");

            // Handle button click event
            factory.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((sender, e) =>
            {
                var button = sender as Button;
                var dataGridRow = FindVisualParent<DataGridRow>(button);

                if (dataGridRow != null && viewModel != null)
                {
                    if (dataGridRow.DataContext is Dictionary<string, object> dataGridRowContext)
                        viewModel.RemoveRowFromDataGrid(dataGridRowContext);
                }
            }));

            template.VisualTree = factory;

            return template;
        }
    }
}
