﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Expression.Interactivity.Core;
using Raven.Imports.Newtonsoft.Json;
using Raven.Imports.Newtonsoft.Json.Linq;
using Raven.Abstractions.Data;
using Raven.Json.Linq;
using Raven.Studio.Commands;
using Raven.Studio.Features.Documents;
using Raven.Studio.Features.Input;
using Raven.Studio.Infrastructure;
using Raven.Studio.Messages;

namespace Raven.Studio.Models
{
    public class EditableDocumentModel : PageViewModel
	{
		private readonly Observable<JsonDocument> document;
		private string jsonData;
		private bool isLoaded;
        private int currentIndex;
        private int totalItems;
		public string DocumentKey { get; private set; }
		private readonly string currentDatabase;
        private DocumentNavigator navigator;
        private ICommand navigateNext;
        private ICommand navigatePrevious;

		public EditableDocumentModel()
		{
			ModelUrl = "/edit";

			References = new ObservableCollection<LinkModel>();
			Related = new BindableCollection<LinkModel>(model => model.Title);
			SearchEnabled = false;

			document = new Observable<JsonDocument>();
			document.PropertyChanged += (sender, args) => UpdateFromDocument();
            InitialiseDocument();

            currentDatabase = Database.Value.Name;
        }

        private void InitialiseDocument()
        {
			document.Value = new JsonDocument
								{
                                     DataAsJson = {{"Name", "..."}},
									Etag = Guid.Empty
								};
        }

        private DocumentNavigator Navigator
        {
            get { return navigator; }
            set
            {
                navigator = value;
                OnPropertyChanged(() => Navigator);
                OnPropertyChanged(() => CanNavigate);
            }
        }

        public ICommand NavigateToNext
        {
            get
            {
                return navigateNext ??
                       (navigateNext = new ActionCommand(() => UrlUtil.Navigate(Navigator.GetUrlForNext())));
            }
        }

        public ICommand NavigateToPrevious
        {
            get
            {
                return navigatePrevious ??
                       (navigatePrevious = new ActionCommand(() => UrlUtil.Navigate(Navigator.GetUrlForPrevious())));
            }
		}

		public override void LoadModelParameters(string parameters)
		{
			var url = new UrlParser(UrlUtil.Url);

			if (url.GetQueryParam("mode") == "new")
			{
				Mode = DocumentMode.New;
                InitialiseDocument();
                Navigator = null;
                CurrentIndex = 0;
                TotalItems = 0;
                SetCurrentDocumentId(null);

				return;
			}

            Navigator = DocumentNavigator.FromUrl(url);

            Navigator.GetDocument().ContinueOnSuccessInTheUIThread(
                result =>
			{
                        if (result.Document == null)
															{
																HandleDocumentNotFound();
																return;
															}

                        if (string.IsNullOrEmpty(result.Document.Key))
			{
				Mode = DocumentMode.Projection;
                            LocalId = Guid.NewGuid().ToString();
				}
                        else
				{
                            Mode = DocumentMode.DocumentWithId;
                            LocalId = result.Document.Key;
                            SetCurrentDocumentId(result.Document.Key);
				}

                        isLoaded = true;
                        document.Value = result.Document;
                        CurrentIndex = (int) result.Index;
                        TotalItems = (int) result.TotalDocuments;
                    })
                .Catch();
			}

		private void HandleDocumentNotFound()
		{
			Notification notification;
			if (Mode == DocumentMode.Projection)
				notification = new Notification("Could not parse projection correctly", NotificationLevel.Error);
			else
                notification = new Notification(string.Format("Could not find '{0}' document", Key),
                                                NotificationLevel.Warning);
			ApplicationModel.Current.AddNotification(notification);
			UrlUtil.Navigate("/documents");
		}

        public int CurrentItemNumber
		{
            get { return CurrentIndex + 1; }
        }

        private int CurrentIndex
			{
            get { return currentIndex; }
            set
				{
                currentIndex = value;
                OnPropertyChanged(() => CurrentItemNumber);
                OnPropertyChanged(() => HasPrevious);
                OnPropertyChanged(() => HasNext);
                OnPropertyChanged(() => CanNavigate);
			}
		}

        public int TotalItems
		{
            get { return totalItems; }
            set
			{
                totalItems = value;
                OnPropertyChanged(() => TotalItems);
                OnPropertyChanged(() => HasPrevious);
                OnPropertyChanged(() => HasNext);
                OnPropertyChanged(() => CanNavigate);
			}
		}

        public bool HasPrevious
		{
            get { return CurrentIndex > 0; }
		}

        public bool HasNext
		{
            get { return CurrentIndex < TotalItems - 1; }
		}

        public bool CanNavigate
		{
            get { return Navigator != null && (HasNext || HasPrevious); }
		}

        public void PutDocumentIdInUrl(string docId)
		{
            if (docId != null && DocumentKey != docId)
                UrlUtil.Navigate("/edit?id=" + docId);

            SetCurrentDocumentId(docId);
			}

        private void SetCurrentDocumentId(string docId)
		{
			DocumentKey = Key = docId;
		}

		private void UpdateFromDocument()
		{
			var newdoc = document.Value;
            RemoveNonDisplayedMetadata(newdoc.Metadata);
			JsonMetadata = newdoc.Metadata.ToString(Formatting.Indented);
			UpdateMetadata(newdoc.Metadata);
			JsonData = newdoc.DataAsJson.ToString(Formatting.Indented);
			UpdateRelated();
			OnEverythingChanged();
		}

        private void RemoveNonDisplayedMetadata(RavenJObject metaData)
        {
            metaData.Remove("@etag");
            metaData.Remove("@id");
        }

		private void UpdateMetadata(RavenJObject metadataAsJson)
		{
			metadata = metadataAsJson.ToDictionary(x => x.Key, x =>
															   {
																   if (x.Value.Type == JTokenType.String)
																	   return x.Value.Value<string>();
																   return x.Value.ToString(Formatting.None);
															   });
			OnPropertyChanged(() => Metadata);
			JsonMetadata = metadataAsJson.ToString(Formatting.Indented);
		}

		public ObservableCollection<LinkModel> References { get; private set; }
		public BindableCollection<LinkModel> Related { get; private set; }

		private bool searchEnabled;
		public bool SearchEnabled
		{
			get { return searchEnabled; }
			set
			{
				searchEnabled = value;
				OnPropertyChanged(() => SearchEnabled);
			}
		}

		private string localId;
		public string LocalId
		{
			get { return localId; }
			set
			{
				localId = value;
				OnPropertyChanged(() => LocalId);
                OnPropertyChanged(() => CurrentItemNumber);
			}
		}

		public string DisplayId
		{
			get
			{
				if (Mode == DocumentMode.Projection)
					return "Projection";
				if (Mode == DocumentMode.New)
					return "New Document";
				return DocumentKey;
			}
		}

		public string Collection
		{
            get { return metadata.FirstOrDefault(x => x.Key == "Raven-Entity-Name").Value; }
		}

		private string jsonMetadata;
		public string JsonMetadata
		{
			get { return jsonMetadata; }
			set
			{
				jsonMetadata = value;
				OnPropertyChanged(() => JsonMetadata);
				OnPropertyChanged(() => DocumentSize);
			}
		}

		private DocumentMode mode = DocumentMode.NotInitializedYet;
		public DocumentMode Mode
		{
			get { return mode; }
			set
			{
				mode = value;
				OnPropertyChanged(() => Mode);
				OnPropertyChanged(() => DisplayId);
			}
		}

		public string JsonData
		{
			get { return jsonData; }
			set
			{
				jsonData = value;
				UpdateReferences();
				OnPropertyChanged(() => JsonData);
				OnPropertyChanged(() => DocumentSize);
			}
		}

		public string DocumentSize
		{
			get
			{
				double byteCount = Encoding.UTF8.GetByteCount(JsonData) + Encoding.UTF8.GetByteCount(JsonMetadata);
				string sizeTerm = "Bytes";
                if (byteCount > 1024*1024)
				{
					sizeTerm = "MBytes";
                    byteCount = byteCount/1024*1024;
				}
				else if (byteCount > 1024)
				{
					sizeTerm = "KBytes";
                    byteCount = byteCount/1024;

				}
				return string.Format("Content-Length: {0:#,#.##;;0} {1}", byteCount, sizeTerm);
			}
		}

		private bool notifiedOnDelete;
		private bool notifiedOnChange;

		protected override Task LoadedTimerTickedAsync()
		{
			if (isLoaded == false ||
				Mode != DocumentMode.DocumentWithId ||
				currentDatabase != Database.Value.Name)
				return null;

			return DatabaseCommands.GetAsync(DocumentKey)
				.ContinueOnSuccess(docOnServer =>
				{
					if (docOnServer == null)
					{
						if (notifiedOnDelete)
							return;
						notifiedOnDelete = true;
                                               ApplicationModel.Current.AddNotification(
                                                   new Notification("Document " + Key + " was deleted on the server"));
					}
					else if (docOnServer.Etag != Etag)
					{
						if (notifiedOnChange)
							return;
						notifiedOnChange = true;
                                               ApplicationModel.Current.AddNotification(
                                                   new Notification("Document " + Key + " was changed on the server"));
					}
				});
		}

		private void UpdateReferences()
		{
			if (Seperator != null)
			{
				var referencesIds = Regex.Matches(jsonData, @"""(\w+" + Seperator + @"\w+)");
				References.Clear();
				foreach (var source in referencesIds.Cast<Match>().Select(x => x.Groups[1].Value).Distinct())
				{
					DateTime time;
					if (DateTime.TryParse(source, out time))
						continue;

					References.Add(new LinkModel
					{
						Title = source,
						HRef = "/Edit?id=" + source
					});
				}
			}
		}

		private void UpdateRelated()
		{
			if (string.IsNullOrEmpty(Key))
				return;
			DatabaseCommands.GetDocumentsStartingWithAsync(Key + Seperator, 0, 15)
				.ContinueOnSuccess(items =>
								   {
									   if (items == null)
										   return;

									   var linkModels = items.Select(doc => new LinkModel
																			{
																				Title = doc.Key,
																				HRef = "/Edit?id=" + doc.Key
																			}).ToArray();
									   Related.Set(linkModels);
								   });
		}


		public string Key
		{
			get { return document.Value.Key; }
			set
			{
				document.Value.Key = value;
				OnPropertyChanged(() => Key);
			}
		}

		public string Seperator
		{
			get
			{
				if (document.Value.Key != null && document.Value.Key.Contains("/"))
					return "/";
				if (document.Value.Key != null && document.Value.Key.Contains("-"))
					return "-";
				return null;
			}
		}

		public Guid? Etag
		{
			get { return document.Value.Etag; }
			set
			{
				document.Value.Etag = value;
				OnPropertyChanged(() => Etag);
				OnPropertyChanged(() => Metadata);
			}
		}

		public DateTime? LastModified
		{
			get { return document.Value.LastModified; }
			set
			{
				document.Value.LastModified = value;
				OnPropertyChanged(() => LastModified);
				OnPropertyChanged(() => Metadata);
			}
		}

		private IDictionary<string, string> metadata;

		public IEnumerable<KeyValuePair<string, string>> Metadata
		{
			get
			{
				return metadata
                    .Where(x => x.Key != "@etag" && x.Key != "@id")
                    .OrderBy(x => x.Key)
					.Concat(new[]
								{
									new KeyValuePair<string, string>("ETag", Etag.HasValue ? Etag.ToString() : ""),
									new KeyValuePair<string, string>("Last-Modified", LastModified.HasValue ? LastModified.ToString(): ""),
								});
			}
		}

		public ICommand Save
		{
			get { return new SaveDocumentCommand(this); }
		}

		public ICommand Delete
		{
			get { return new DeleteDocumentCommand(Key, true); }
		}

		public ICommand Prettify
		{
			get { return new PrettifyDocumentCommand(this); }
		}

		public ICommand Refresh
		{
			get { return new RefreshDocumentCommand(this); }
		}

		public ICommand EnableSearch
		{
			get { return new ChangeFieldValueCommand<EditableDocumentModel>(this, x => x.SearchEnabled = true); }
		}

		public ICommand DisableSearch
		{
			get { return new ChangeFieldValueCommand<EditableDocumentModel>(this, x => x.SearchEnabled = false); }
		}

		public ICommand ToggleSearch
		{
			get { return new ChangeFieldValueCommand<EditableDocumentModel>(this, x => x.SearchEnabled = !x.searchEnabled); }
		}

		private class RefreshDocumentCommand : Command
		{
			private readonly EditableDocumentModel parent;

			public RefreshDocumentCommand(EditableDocumentModel parent)
			{
				this.parent = parent;
			}

			public override bool CanExecute(object parameter)
			{
				return string.IsNullOrWhiteSpace(parent.DocumentKey) == false;
			}

			public override void Execute(object _)
			{
				parent.DatabaseCommands.GetAsync(parent.DocumentKey)
					.ContinueOnSuccess(doc =>
										{
											if (doc == null)
											{
												parent.HandleDocumentNotFound();
												return;
											}

											parent.document.Value = doc;
										})
					.Catch();
			}
		}

		private class SaveDocumentCommand : Command
		{
			private readonly EditableDocumentModel document;

			public string Seperator
			{
				get
				{
					if (document.Key.Contains("/"))
						return "/";
					if (document.Key.Contains("-"))
						return "-";
					return null;
				}
			}

			public SaveDocumentCommand(EditableDocumentModel document)
			{
				this.document = document;
			}

			public override void Execute(object parameter)
			{
				if (document.Key != null && document.Key.StartsWith("Raven/", StringComparison.InvariantCultureIgnoreCase))
				{
					AskUser.ConfirmationAsync("Confirm Edit", "Are you sure that you want to edit a system document?")
						.ContinueWhenTrue(SaveDocument);
					return;
				}

				SaveDocument();
			}

			private void SaveDocument()
			{
				RavenJObject doc;
				RavenJObject metadata;

				try
				{
					doc = RavenJObject.Parse(document.JsonData);
					metadata = RavenJObject.Parse(document.JsonMetadata);
					if (document.Key != null && Seperator != null && metadata.Value<string>(Constants.RavenEntityName) == null)
					{
						var entityName = document.Key.Split(new[] { Seperator }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

						if (entityName != null && entityName.Length > 1)
						{
							metadata[Constants.RavenEntityName] = char.ToUpper(entityName[0]) + entityName.Substring(1);
						}
						else
						{
							metadata[Constants.RavenEntityName] = entityName;
						}
					}
				}
				catch (JsonReaderException ex)
				{
					ErrorPresenter.Show(ex.Message);
					return;
				}

				document.UpdateMetadata(metadata);
				ApplicationModel.Current.AddNotification(new Notification("Saving document " + document.Key + " ..."));
				DatabaseCommands.PutAsync(document.Key, document.Etag, doc, metadata)
					.ContinueOnSuccess(result =>
					{
						ApplicationModel.Current.AddNotification(new Notification("Document " + result.Key + " saved"));
						document.Etag = result.ETag;
						document.PutDocumentIdInUrl(result.Key);
					})
					.ContinueOnSuccess(() => new RefreshDocumentCommand(document).Execute(null))
					.Catch(exception => ApplicationModel.Current.AddNotification(new Notification(exception.Message)));
			}
		}

		private class PrettifyDocumentCommand : Command
		{
			private readonly EditableDocumentModel editableDocumentModel;

			public PrettifyDocumentCommand(EditableDocumentModel editableDocumentModel)
			{
				this.editableDocumentModel = editableDocumentModel;
			}

			public override void Execute(object parameter)
			{
				RavenJObject metadata;
				try
				{
					metadata = RavenJObject.Parse(editableDocumentModel.JsonMetadata);
					editableDocumentModel.JsonData = RavenJObject.Parse(editableDocumentModel.JsonData).ToString(Formatting.Indented);
					editableDocumentModel.JsonMetadata = metadata.ToString(Formatting.Indented);
				}
				catch (JsonReaderException ex)
				{
					ErrorPresenter.Show(ex.Message);
					return;
				}
				editableDocumentModel.UpdateMetadata(metadata);
			}
		}
	}

	public enum DocumentMode
	{
		NotInitializedYet,
		DocumentWithId,
		Projection,
		New,
	}
}
