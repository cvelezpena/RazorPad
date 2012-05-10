﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using RazorPad.Framework;
using RazorPad.Persistence;
using RazorPad.UI;
using RazorPad.UI.ModelBuilders;
using RazorPad.UI.Theming;

namespace RazorPad.ViewModels
{
    [Export]
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly RazorDocumentManager _documentManager;
        private readonly ModelProviders _modelProviders;
        private readonly ModelBuilders _modelBuilders;

        public event EventHandler<EventArgs<string>> Error;

        public ICommand AnchorableCloseCommand { get; private set; }
        public ICommand CloseCommand { get; private set; }
        public ICommand ManageReferencesCommand { get; private set; }
        public ICommand NewCommand { get; private set; }
        public ICommand OpenCommand { get; private set; }
        public ICommand SaveCommand { get; private set; }
        public ICommand SaveAsCommand { get; private set; }
        public ICommand SwitchThemeCommand { get; private set; }

        // Use thunks to create test seams
        public Func<RazorTemplateEditorViewModel, MessageBoxResult> ConfirmSaveDirtyDocumentThunk =
            MessageBoxHelpers.ShowConfirmSaveDirtyDocumentMessageBox;

        public Func<string> GetOpenFilenameThunk =
            MessageBoxHelpers.ShowOpenFileDialog;

        public Func<IEnumerable<string>, IEnumerable<string>> GetReferencesThunk =
            references => references;

        public Func<RazorTemplateEditorViewModel, string> GetSaveAsFilenameThunk =
            MessageBoxHelpers.ShowSaveAsDialog;

        public Action<string> ShowErrorThunk =
            MessageBoxHelpers.ShowErrorMessageBox;

        public Action<string> LoadThemeFromFileThunk =
            filename =>
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        var dic = (ResourceDictionary) XamlReader.Load(stream);
                        Application.Current.Resources.MergedDictionaries.Clear();
                        Application.Current.Resources.MergedDictionaries.Add(dic);
                    }
                };


        public RazorTemplateEditorViewModel CurrentTemplate
        {
            get { return _currentTemplate; }
            set
            {
                if (_currentTemplate == value)
                    return;

                _currentTemplate = value;
                OnPropertyChanged("CurrentTemplate");
                OnPropertyChanged("HasCurrentTemplate");
            }
        }
        private RazorTemplateEditorViewModel _currentTemplate;

        public bool HasCurrentTemplate
        {
            get { return CurrentTemplate != null; }
        }

        public ObservableCollection<RazorTemplateEditorViewModel> TemplateEditors
        {
            get;
            private set;
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set
            {
                if (_statusMessage == value)
                    return;

                _statusMessage = value;
                OnPropertyChanged("StatusMessage");
            }
        }
        private string _statusMessage;

        public ObservableTextWriter Messages { get; set; }

        public ObservableCollection<Theme> Themes
        {
            get { return _themes; }
            set
            {
                if (_themes == value)
                    return;
                _themes = value;
                OnPropertyChanged("Themes");
            }
        }
        private ObservableCollection<Theme> _themes;



        [ImportingConstructor]
        public MainWindowViewModel(RazorDocumentManager documentManager, ModelProviders modelProviders, ModelBuilders modelBuilders)
        {
            _documentManager = documentManager;
            _modelBuilders = modelBuilders;
            _modelProviders = modelProviders;

            TemplateEditors = new ObservableCollection<RazorTemplateEditorViewModel>();

            RegisterCommands();
        }


        private void RegisterCommands()
        {
            AnchorableCloseCommand = new RelayCommand(
                () => { /* Ignore */ },
                () => false);

            CloseCommand = new RelayCommand(
                    p => Close(CurrentTemplate),
                    p => HasCurrentTemplate
                );

            ManageReferencesCommand = new RelayCommand(() => {
                var loadedReferences = CurrentTemplate.AssemblyReferences;
                CurrentTemplate.AssemblyReferences = GetReferencesThunk(loadedReferences);
            });

            NewCommand = new RelayCommand(() => AddNewTemplateEditor());

            OpenCommand = new RelayCommand(p => {
                var filename = GetOpenFilenameThunk();
                AddNewTemplateEditor(filename);
            });

            SaveCommand = new RelayCommand(
                    p => Save(CurrentTemplate),
                    p => HasCurrentTemplate
                );

            SaveAsCommand = new RelayCommand(
                    p => SaveAs(CurrentTemplate.Document),
                    p => HasCurrentTemplate && CurrentTemplate.CanSaveAsNewFilename
                );

            SwitchThemeCommand = new RelayCommand(
                    p => SwitchTheme((Theme)p),
                    p => true
                );
        }

        private void SwitchTheme(Theme theme)
        {
            LoadThemeFromFileThunk(theme.FilePath);
            Themes.ToList().ForEach(x => x.Selected = false);
            theme.Selected = true;
        }

        public void AddNewTemplateEditor(bool current = true)
        {
            var modelProvider = ModelProviders.DefaultFactory.Create();

            var document = new RazorDocument { ModelProvider = modelProvider };

            AddNewTemplateEditor(document, current);
        }

        public void AddNewTemplateEditor(string filename, bool current = true)
        {
            if (string.IsNullOrWhiteSpace(filename))
            {
                Trace.TraceWarning("AddNewTemplateEditor called without specifying a filename -- returning");
                return;
            }

            RazorTemplateEditorViewModel loadedTemplate =
                TemplateEditors
                    .Where(x => !string.IsNullOrWhiteSpace(x.Filename))
                    .SingleOrDefault(x => x.Filename.Equals(filename, StringComparison.OrdinalIgnoreCase));

            if (loadedTemplate != null)
            {
                if (current)
                    CurrentTemplate = loadedTemplate;

                return;
            }

            var document = _documentManager.Load(filename);

            AddNewTemplateEditor(document, current);
        }

        public void AddNewTemplateEditor(RazorDocument document, bool current = true)
        {
            AddNewTemplateEditor(new RazorTemplateEditorViewModel(document, _modelBuilders, _modelProviders), current);
        }

        public void AddNewTemplateEditor(RazorTemplateEditorViewModel templateEditor, bool current = true)
        {
            templateEditor.OnStatusUpdated += (sender, args) =>
                                                  {
                                                      Trace.TraceInformation(args.Message);
                                                      StatusMessage = args.Message;
                                                  };

            templateEditor.Messages = Messages;

            TemplateEditors.Add(templateEditor);

            if (current)
                CurrentTemplate = templateEditor;

            templateEditor.Execute();
        }


        public void Close(RazorTemplateEditorViewModel document, bool? save = null)
        {
            if (document.IsDirty && save.GetValueOrDefault(true))
            {
                var shouldSave = ConfirmSaveDirtyDocumentThunk(document);

                switch (shouldSave)
                {
                    case MessageBoxResult.Cancel:
                        return;

                    case MessageBoxResult.Yes:
                        Save(document);
                        break;
                }
            }

            TemplateEditors.Remove(document);
        }

        public void Save(RazorTemplateEditorViewModel document)
        {
            var filename = document.Filename;

            if (!document.CanSaveToCurrentlyLoadedFile)
                filename = null;

            var newFilename = SaveAs(document.Document, filename);

            document.Filename = newFilename;
        }

        public string SaveAs(RazorDocument document, string filename = null)
        {
            try
            {
                if (filename == null)
                    filename = GetSaveAsFilenameThunk(CurrentTemplate);

                if (string.IsNullOrWhiteSpace(filename))
                {
                    Trace.TraceWarning("Filename is empty - skipping save");
                }
                else
                    _documentManager.Save(document, filename);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Error saving document: {0}", ex);
                Error.SafeInvoke(ex.Message);
            }

            return filename;
        }
    }
}
