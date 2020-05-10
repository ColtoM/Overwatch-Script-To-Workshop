using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Deltin.Deltinteger.Parse;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

using MediatR;

namespace Deltin.Deltinteger.LanguageServer
{
    public class DocumentHandler : ITextDocumentSyncHandler
    {
        // Static
        private static readonly TextDocumentSyncKind _syncKind = TextDocumentSyncKind.Incremental;
        private static readonly bool _sendTextOnSave = true;

        // Object
        public List<TextDocumentItem> Documents { get; } = new List<TextDocumentItem>();
        private DeltintegerLanguageServer _languageServer { get; } 
        private SynchronizationCapability _compatibility;

        public DocumentHandler(DeltintegerLanguageServer languageServer)
        {
            _languageServer = languageServer;
        }

        public TextDocumentAttributes GetTextDocumentAttributes(Uri uri)
        {
            return new TextDocumentAttributes(uri, "ostw");
        }

        // Text document registeration options.
        TextDocumentRegistrationOptions IRegistration<TextDocumentRegistrationOptions>.GetRegistrationOptions() =>  new TextDocumentRegistrationOptions() { 
            DocumentSelector = DeltintegerLanguageServer.DocumentSelector 
        };

        // Save options.
        TextDocumentSaveRegistrationOptions IRegistration<TextDocumentSaveRegistrationOptions>.GetRegistrationOptions() => new TextDocumentSaveRegistrationOptions() {
            DocumentSelector = DeltintegerLanguageServer.DocumentSelector,
            IncludeText = _sendTextOnSave
        };

        // Document change options.
        TextDocumentChangeRegistrationOptions IRegistration<TextDocumentChangeRegistrationOptions>.GetRegistrationOptions() => new TextDocumentChangeRegistrationOptions() {
            DocumentSelector = DeltintegerLanguageServer.DocumentSelector,
            SyncKind = _syncKind
        };

        // Handle save.
        public Task<Unit> Handle(DidSaveTextDocumentParams saveParams, CancellationToken token)
        {
            if (_sendTextOnSave)
            {
                var document = TextDocumentFromUri(saveParams.TextDocument.Uri);
                document.Text = saveParams.Text;
                return Parse(document);
            }
            else return Parse(saveParams.TextDocument.Uri);
        }

        // Handle close.
        public Task<Unit> Handle(DidCloseTextDocumentParams closeParams, CancellationToken token)
        {
            Documents.Remove(TextDocumentFromUri(closeParams.TextDocument.Uri));
            return Unit.Task;
        }

        // Handle open.
        public Task<Unit> Handle(DidOpenTextDocumentParams openParams, CancellationToken token)
        {
            Documents.Add(openParams.TextDocument);
            return Parse(openParams.TextDocument.Uri);
        }

        // Handle change.
        public Task<Unit> Handle(DidChangeTextDocumentParams changeParams, CancellationToken token)
        {
            var document = TextDocumentFromUri(changeParams.TextDocument.Uri);
            foreach (var change in changeParams.ContentChanges)
            {
                int start = PosIndex(document.Text, change.Range.Start);
                int length = PosIndex(document.Text, change.Range.End) - start;

                StringBuilder rep = new StringBuilder(document.Text);
                rep.Remove(start, length);
                rep.Insert(start, change.Text);

                document.Text = rep.ToString();
                document.Version = changeParams.TextDocument.Version;
            }
            return Parse(document);
        }

        // Get client compatibility
        void ICapability<SynchronizationCapability>.SetCapability(SynchronizationCapability compatibility)
        {
            _compatibility = compatibility;
        }

        public TextDocumentItem TextDocumentFromUri(Uri uri)
        {
            for (int i = 0; i < Documents.Count; i++)
                // TODO-URI: Should use Uri.Compare? 
                if (Documents[i].Uri == uri)
                    return Documents[i];
            return null;
        }

        private static int PosIndex(string text, Position pos)
        {
            if (pos.Line == 0 && pos.Character == 0) return 0;

            int line = 0;
            int character = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    character = 0;
                }
                else
                {
                    character++;
                }

                if (pos.Line == line && pos.Character == character)
                    return i + 1;
                
                if (line > pos.Line)
                    throw new Exception();
            }
            throw new Exception();
        }

        Task<Unit> Parse(Uri uri) => Parse(TextDocumentFromUri(uri));
        Task<Unit> Parse(TextDocumentItem document)
        {
            _typeWait.Restart();

            lock (_parseItemLock) _parseItem = document;

            if (!_updateTaskIsRunning)
            {
                _updateTaskIsRunning = true;
                _updateTask = Task.Run(Update);
            }
            return Unit.Task;
        }

        private const int TimeToUpdate = 250;
        private TextDocumentItem _parseItem;
        private object _parseItemLock = new object();
        private Stopwatch _typeWait = new Stopwatch();
        private object _parseLock = new object();
        private bool _updateTaskIsRunning = false;
        private Task<Unit> _updateTask = null;

        Unit Update()
        {
            SpinWait.SpinUntil(() => {
                lock (_parseLock) return _typeWait.ElapsedMilliseconds >= TimeToUpdate;
            });

            lock (_parseLock)
            {   
                try
                {
                    Diagnostics diagnostics = new Diagnostics();
                    ScriptFile root;
                    lock (_parseItemLock) root = new ScriptFile(diagnostics, _parseItem.Uri, _parseItem.Text);
                    DeltinScript deltinScript = new DeltinScript(new TranslateSettings(diagnostics, root, _languageServer.FileGetter) {
                        OutputLanguage = _languageServer.ConfigurationHandler.OutputLanguage,
                        OptimizeOutput = _languageServer.ConfigurationHandler.OptimizeOutput
                    });
                    _languageServer.LastParse = deltinScript;

                    // Publish the diagnostics.
                    var publishDiagnostics = diagnostics.GetDiagnostics();
                    foreach (var publish in publishDiagnostics)
                        _languageServer.Server.Document.PublishDiagnostics(publish);
                    
                    if (deltinScript.WorkshopCode != null)
                    {
                        _languageServer.Server.SendNotification(DeltintegerLanguageServer.SendWorkshopCode, deltinScript.WorkshopCode);
                        _languageServer.Server.SendNotification(DeltintegerLanguageServer.SendElementCount, deltinScript.ElementCount.ToString());
                    }
                    else
                    {
                        _languageServer.Server.SendNotification(DeltintegerLanguageServer.SendWorkshopCode, diagnostics.OutputDiagnostics());
                        _languageServer.Server.SendNotification(DeltintegerLanguageServer.SendElementCount, "-");
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "An exception was thrown while parsing.");
                    _languageServer.Server.SendNotification(DeltintegerLanguageServer.SendWorkshopCode, "An exception was thrown while parsing.\r\n" + ex.ToString());
                    _languageServer.Server.SendNotification(DeltintegerLanguageServer.SendElementCount, "-");
                }
                finally
                {
                    _updateTaskIsRunning = false;
                    _typeWait.Stop();
                }
                return Unit.Value;
            }
        }

        public void WaitForNextUpdate()
        {
            SpinWait.SpinUntil(() => { lock(_parseLock) return !_updateTaskIsRunning; });
        }
    }
}