﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Xml.Linq;
using Caliburn.Micro;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using LIC.Malone.Client.Desktop.Controls;
using LIC.Malone.Client.Desktop.Extensions;
using LIC.Malone.Client.Desktop.Messages;
using LIC.Malone.Core;
using LIC.Malone.Core.Authentication;
using LIC.Malone.Core.Authentication.OAuth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace LIC.Malone.Client.Desktop.ViewModels
{
	// TODO: Actually conduct the flyout.
	public class AppViewModel : Conductor<object>, IHandle<TokenAdded>
	{
		private readonly IWindowManager _windowManager;
		private readonly IEventAggregator _bus;
		private readonly DialogManager _dialogManager = new DialogManager();
		private readonly List<string> _allowedSchemes = new List<string> { Uri.UriSchemeHttp, Uri.UriSchemeHttps };

		private AddCollectionViewModel _addCollectionViewModel = new AddCollectionViewModel();
		private AddTokenViewModel _addTokenViewModel;
		private readonly NamedAuthorizationState _anonymousToken = new NamedAuthorizationState("<Anonymous>", null);

		private string _historyJsonPath;

		#region Databound properties

		private IObservableCollection<Request> _history = new BindableCollection<Request>();
		public IObservableCollection<Request> History
		{
			get { return _history; }
			set
			{
				_history = value;
				NotifyOfPropertyChange(() => History);
			}
		}

		private double _historyHorizontalOffset;
		public double HistoryHorizontalOffset
		{
			get { return _historyHorizontalOffset; }
			set
			{
				_historyHorizontalOffset = value;
				NotifyOfPropertyChange(() => HistoryHorizontalOffset);
			}
		}

		private double _historyVerticalScrollBarOffset;
		public double HistoryVerticalScrollBarOffset
		{
			get { return _historyVerticalScrollBarOffset; }
			set
			{
				_historyVerticalScrollBarOffset = value;
				NotifyOfPropertyChange(() => HistoryVerticalScrollBarOffset);
			}
		}

		private Request _selectedHistory;
		public Request SelectedHistory
		{
			get { return _selectedHistory; }
			set
			{
				_selectedHistory = value;
				NotifyOfPropertyChange(() => SelectedHistory);
			}
		}

		public Visibility DirtyVisibility
		{
			get { return IsRequestDirty() ? Visibility.Visible : Visibility.Hidden; }
		}

		private string _url;
		public string Url
		{
			get { return _url; }
			set
			{
				_url = value;
				NotifyOfPropertyChange(() => Url);
				NotifyOfPropertyChange(() => DirtyVisibility);
				NotifyOfPropertyChange(() => CanSend);
			}
		}

		private IEnumerable<Method> _methods = new List<Method>
		{
			Method.GET,
			Method.POST,
			Method.PUT
		};

		public IEnumerable<Method> Methods
		{
			get { return _methods; }
			set
			{
				_methods = value;
				NotifyOfPropertyChange(() => Methods);
			}
		}

		private Method _selectedMethod;
		public Method SelectedMethod
		{
			get { return _selectedMethod; }
			set
			{
				_selectedMethod = value;
				NotifyOfPropertyChange(() => SelectedMethod);
				NotifyOfPropertyChange(() => DirtyVisibility);
			}
		}

		private static IEnumerable<string> _accepts = new List<string>
		{
			"text/xml",
			"application/json"
		};
		
		public IEnumerable<string> Accepts
		{
			get { return _accepts; }
			set
			{
				_accepts = value;
				NotifyOfPropertyChange(() => Accepts);
			}
		}

		private string _selectedAccept;
		public string SelectedAccept
		{
			get { return _selectedAccept; }
			set
			{
				_selectedAccept = value;

				// Must be a better way to do this.
				var headers = new BindableCollection<Header>(Headers);
				var header = headers.Single(h => h.Name == Header.Accept);
				header.Value = _selectedAccept;
				Headers = headers;

				NotifyOfPropertyChange(() => SelectedAccept);
			}
		}

		private static IEnumerable<string> _contentTypes = new List<string>
		{
			"text/xml",
			"application/json"
		};

		public IEnumerable<string> ContentTypes
		{
			get { return _contentTypes; }
			set
			{
				_contentTypes = value;
				NotifyOfPropertyChange(() => ContentTypes);
			}
		}

		private string _selectedContentType;
		public string SelectedContentType
		{
			get { return _selectedContentType; }
			set
			{
				_selectedContentType = value;

				// Must be a better way to do this.
				var headers = new BindableCollection<Header>(Headers);
				var header = headers.Single(h => h.Name == Header.ContentType);
				header.Value = _selectedContentType;
				Headers = headers;

				NotifyOfPropertyChange(() => SelectedContentType);
			}
		}

		private IObservableCollection<Header> _headers = new BindableCollection<Header>(new List<Header> { new Header(Header.Accept, _accepts.First()), new Header(Header.ContentType, _contentTypes.First()) });
		public IObservableCollection<Header> Headers
		{
			get { return _headers; }
			set
			{
				_headers = value;
				NotifyOfPropertyChange(() => Headers);
			}
		}

		private IObservableCollection<Header> _responseHeaders;
		public IObservableCollection<Header> ResponseHeaders
		{
			get { return _responseHeaders; }
			set
			{
				_responseHeaders = value;
				NotifyOfPropertyChange(() => ResponseHeaders);
			}
		}

		private IObservableCollection<NamedAuthorizationState> _tokens;
		public IObservableCollection<NamedAuthorizationState> Tokens
		{
			get { return _tokens; }
			set
			{
				_tokens = value;
				NotifyOfPropertyChange(() => Tokens);
			}
		}

		private NamedAuthorizationState _selectedToken;
		public NamedAuthorizationState SelectedToken
		{
			get { return _selectedToken; }
			set
			{
				_selectedToken = value;
				NotifyOfPropertyChange(() => SelectedToken);
				NotifyOfPropertyChange(() => SelectedTokenJson);
			}
		}

		private TextDocument _selectedTokenJson = new TextDocument();
		public TextDocument SelectedTokenJson
		{
			get
			{
				if (SelectedToken == null)
					_selectedTokenJson.Text = string.Empty;
				else if (SelectedToken.AuthorizationState == null)
					_selectedTokenJson.Text = string.Empty;
				else
					_selectedTokenJson.Text = JsonConvert.SerializeObject(SelectedToken.AuthorizationState, Formatting.Indented); ;

				return _selectedTokenJson;
			}
		}

		private HttpStatusCode? _httpStatusCode;
		public HttpStatusCode? HttpStatusCode
		{
			get { return _httpStatusCode; }
			set
			{
				_httpStatusCode = value;
				NotifyOfPropertyChange(() => HttpStatusCode);
			}
		}

		private TextDocument _requestBody = new TextDocument();
		public TextDocument RequestBody
		{
			get { return _requestBody; }
			set
			{
				_requestBody = value;
				NotifyOfPropertyChange(() => RequestBody);
			}
		}

		private Visibility _requestHasResponseVisibility;
		public Visibility RequestHasResponseVisibility
		{
			get { return _requestHasResponseVisibility; }
			set
			{
				_requestHasResponseVisibility = value;
				NotifyOfPropertyChange(() => RequestHasResponseVisibility);
				NotifyOfPropertyChange(() => RequestHasNoResponseVisibility);
			}
		}

		public Visibility RequestHasNoResponseVisibility
		{
			get { return RequestHasResponseVisibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible; }
		}

		private TextDocument _responseBody = new TextDocument();
		public TextDocument ResponseBody
		{
			get
			{
				_responseBody.Text = Prettify(_responseBody.Text, ResponseContentType);
				return _responseBody;
			}
			set
			{
				_responseBody.Text = Prettify(_responseBody.Text, ResponseContentType);
				_responseBody = value;
				NotifyOfPropertyChange(() => ResponseBody);
			}
		}

		private string _responseContentType;
		public string ResponseContentType
		{
			get { return _responseContentType; }
			set
			{
				_responseContentType = value;
				NotifyOfPropertyChange(() => ResponseContentType);
				NotifyOfPropertyChange(() => ResponseBodyHighlighting);
			}
		}

		private string _responseTime;
		public string ResponseTime
		{
			get { return _responseTime; }
			set
			{
				_responseTime = value;
				NotifyOfPropertyChange(() => ResponseTime);
			}
		}

		public IHighlightingDefinition ResponseBodyHighlighting
		{
			get
			{
				return GetHighlightingForContentType(ResponseContentType);
			}
		}

		public bool CanSend
		{
			get
			{
				Uri url;
				return Uri.TryCreate(Url, UriKind.Absolute, out url) && _allowedSchemes.Contains(url.Scheme);
			}
		}

		#endregion
		
		public AppViewModel()
		{
			_windowManager = IoC.Get<WindowManager>();
			_bus = IoC.Get<EventAggregator>();
			_bus.Subscribe(this);

			_addTokenViewModel = new AddTokenViewModel(_bus, _windowManager);

			Tokens = new BindableCollection<NamedAuthorizationState>(new List<NamedAuthorizationState> { _anonymousToken });
			SelectedToken = _anonymousToken;

			DisplayRequest(new Request());

			LoadConfig();
		}

		private void DisplayRequest(Request request)
		{
			var accept = request.Headers.GetValue(Header.Accept);

			if (!Accepts.Contains(accept))
				accept = Accepts.First();

			var contentType = request.Headers.GetValue(Header.ContentType);

			if (!ContentTypes.Contains(contentType))
				contentType = ContentTypes.First();

			SelectedMethod = request.Method;
			Url = request.Url;
			SelectedAccept = accept;
			RequestBody.Text = Prettify(request.Body, contentType);
			SelectedContentType = contentType;

			var token = request.NamedAuthorizationState;

			if (token == null || token.AuthorizationState == null)
			{
				SelectedToken = _anonymousToken;
			}
			else
			{
				if (Tokens.All(t => t.Guid != token.Guid))
					Tokens.Add(token);

				SelectedToken = token;
			}

			var response = request.Response;
			var hasResponse = response != null;

			RequestHasResponseVisibility = hasResponse ? Visibility.Visible : Visibility.Collapsed;

			if (!hasResponse)
				return;

			ResponseTime = request.ResponseTime;
			ResponseBody.Text = Prettify(response.Body, response.ContentType);
			ResponseContentType = response.ContentType;
			HttpStatusCode = response.HttpStatusCode;
			ResponseHeaders = new BindableCollection<Header>(response.Headers);
		}

		private bool IsRequestDirty()
		{
			var request = SelectedHistory;

			if (request == null)
				return false;

			// TODO: Compare against raw requests.

			if (SelectedMethod != request.Method)
				return true;

			if (Url != request.Url)
				return true;
			
			return false;
		}

		private void LoadConfig()
		{
			var configLocation = ConfigurationManager.AppSettings["ConfigLocation"];

			// TODO: Handle the case of no config.
			if (string.IsNullOrWhiteSpace(configLocation))
				return;

			var applications = new List<OAuthApplication>();
			var authenticationUrls = new List<Uri>();
			var userCredentials = new UserCredentials
			{
				Username = string.Empty,
				Password = string.Empty
			};

			var path = Path.Combine(configLocation, "oauth-applications.json");

			if (File.Exists(path))
				applications = JsonConvert.DeserializeObject<List<OAuthApplication>>(File.ReadAllText(path));

			path = Path.Combine(configLocation, "oauth-authentication-urls.json");

			if (File.Exists(path))
				authenticationUrls = JsonConvert
					.DeserializeObject<List<string>>(File.ReadAllText(path))
					.Select(url => new Uri(url))
					.ToList();

			path = Path.Combine(configLocation, "oauth-user-credentials.json");

			if (File.Exists(path))
				userCredentials = JsonConvert.DeserializeObject<UserCredentials>(File.ReadAllText(path));

			_historyJsonPath = Path.Combine(configLocation, "history.json");

			if (File.Exists(_historyJsonPath))
			{
				var json = File.ReadAllText(_historyJsonPath);

				if (json.Any())
				{
					var history = JsonConvert.DeserializeObject<List<Request>>(json);
					History.AddRange(history);
				}
			}

			_bus.PublishOnUIThread(new ConfigurationLoaded(applications, authenticationUrls, userCredentials));
		}

		public async void Send()
		{
			if (string.IsNullOrWhiteSpace(Url))
				return;

			SelectedHistory = null;

			RequestHasResponseVisibility = Visibility.Collapsed;

			// There was something strange going on with Headers being a BindableCollection or something - it would somehow overwrite the headers
			// for previous requests in the History when just calling Headers.ToList(). This is begging for a unit test, but for now make a copy.
			var headers = Headers.Select(h => new Header(h.Name, h.Value)).ToList();

			var request = new Request(Url, SelectedMethod)
			{
				Headers = headers,
				Body = RequestBody.Text
			};

			if (SelectedToken != null)
				request.NamedAuthorizationState = SelectedToken;

			var client = new ApiClient();
			var result = client.Send(request);
			var response = result.Response;

			var responseError = GetResponseError(response);

			if (responseError != null)
			{
				var dialogResult = await _dialogManager.Show("Oh dear", "Nope, that didn't work.\n\n" + responseError);
				return;
			}

			request.At = result.SentAt;

			var responseHeaders = response
				.Headers
				.Where(p => p.Type == ParameterType.HttpHeader)
				.Select(p => new Header(p.Name, p.Value.ToString()))
				.ToList();

			request.Response = new Response
			{
				Guid = Guid.NewGuid(),
				At = result.ReceivedAt,
				HttpStatusCode = response.StatusCode,
				Body = response.Content,
				ContentType = response.ContentType,
				Headers = responseHeaders
			};

			AddToHistory(request);

			DisplayRequest(request);
		}

		private enum ContentType
		{
			Unknown,
			Xml,
			Json
		}

		private ContentType GetContentType(string contentType)
		{
			if (contentType == null)
				return ContentType.Unknown;

			var parts = contentType
				   .Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries)
				   .Select(s => s.Trim())
				   .ToList();

			if (parts.Contains("application/xml") || parts.Contains("text/xml"))
				return ContentType.Xml;

			if (parts.Contains("application/json"))
				return ContentType.Json;

			return ContentType.Unknown;
		}

		private IHighlightingDefinition GetHighlightingForContentType(string contentType)
		{
			return GetHighlightingForContentType(GetContentType(contentType));
		}

		private IHighlightingDefinition GetHighlightingForContentType(ContentType contentType)
		{
			var manager = HighlightingManager.Instance;
			
			switch (contentType)
			{
				case ContentType.Xml:
					return manager.GetDefinition("XML");

				case ContentType.Json:
					return manager.GetDefinition("JavaScript");

				default:
					return null; 
			}
		}

		private void AddToHistory(Request request)
		{
			History.Insert(0, request);
			NotifyOfPropertyChange(() => History);
			SelectedHistory = History.First();
			SaveHistory();
		}

		private string GetResponseError(IRestResponse response)
		{
			if (response == null)
				return "Uhm, response was null?";

			var sb = new StringBuilder();
			
			switch (response.ResponseStatus)
			{
				case ResponseStatus.None:
					sb.AppendLine("Apparently, none.");
					break;
				case ResponseStatus.Completed:
					return null;
				case ResponseStatus.Error:
					sb.AppendLine("Error.");
					break;
				case ResponseStatus.TimedOut:
					sb.AppendLine("Timed out.");
					break;
				case ResponseStatus.Aborted:
					sb.AppendLine("Aborted.");
					break;
				default:
					return null;
			}

			sb.AppendLine();

			if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
				sb.AppendLine(response.ErrorMessage);

			if (response.ErrorException != null)
			{
				sb.AppendLine(response.ErrorException.Message);
				sb.AppendLine(response.ErrorException.StackTrace);
			}
			
			return sb.ToString();
		}

		public void HistoryLayoutUpdated(object e)
		{
			var listBox = (MaloneListBox) ((ActionExecutionContext) e).Source;

			HistoryHorizontalOffset = listBox.ScrollViewerHorizontalOffset;
			HistoryVerticalScrollBarOffset = listBox.VerticalScrollBarVisibility == Visibility.Visible
				? -14
				: 0;
		}

		public void HistoryClicked(object e)
		{
			if (SelectedHistory == null)
				return;

			DisplayRequest(SelectedHistory);
		}

		public void RemoveFromHistory(object e)
		{
			var request = (Request) e;
			History.Remove(request);
			SaveHistory();
		}

		public void RemoveFromHeaders(object e)
		{
			var header = (Header) e;
			Headers.Remove(header);
		}

		public void AddHeader()
		{
			Headers.Add(new Header());
		}

		public void ClearHistory(object e)
		{
			History = new BindableCollection<Request>();
			SaveHistory();
		}

		private void SaveHistory()
		{
			var json = JsonConvert.SerializeObject(History, Formatting.Indented);
			File.WriteAllText(_historyJsonPath, json);
		}

		public void AddCollection()
		{
			var settings = new Dictionary<string, object>
			{
				{"Title", "Add collection"},
				{"WindowStartupLocation", WindowStartupLocation.CenterOwner}
			};

			_windowManager.ShowDialog(_addCollectionViewModel, settings: settings);
			ActivateItem(_addCollectionViewModel);
		}

		public void AddToken()
		{
			var settings = new Dictionary<string, object>
			{
				{"WindowStartupLocation", WindowStartupLocation.CenterOwner}
			};

			_windowManager.ShowDialog(_addTokenViewModel, settings: settings);
			ActivateItem(_addTokenViewModel);
		}

		public void Reset()
		{
			SelectedHistory = null;
			DisplayRequest(new Request());
		}

		public void Handle(TokenAdded message)
		{
			Tokens.Add(message.NamedAuthorizationState);
			SelectedToken = Tokens.Last();
		}

		private string Prettify(string content, string contentType)
		{
			content = content ?? string.Empty;

			if (string.IsNullOrWhiteSpace(content))
				return content;

			var type = GetContentType(contentType);

			if (type == ContentType.Xml)
			{
				try
				{
					return XDocument.Parse(content).ToString();
				}
				catch
				{
					return content;
				}
			}

			if (type == ContentType.Json)
			{
				try
				{
					var json = JObject.Parse(content);
					return json.ToString(Formatting.Indented);
				}
				catch
				{
					return content;
				}
			}

			// For ContentType.Unknown, the parameter "content" is returned as-is for the out parameter "indentedContent".
			return content;
		}

		public void WindowResized(ActionExecutionContext context)
		{
			// TODO: bug where you maximize, reduce history column to min-width, then un-maximize: the request column will be off the window.
			//var window = context.Source as Window;

			//if (window == null)
			//	return;

			//var grid = window.FindName("MainGrid") as Grid;
			//var historyColumn = window.FindName("HistoryColumn") as ColumnDefinition;
			////var requestColumn = window.FindName("HistoryColumn") as ColumnDefinition;

			//if (grid == null || historyColumn == null)
			//	return;

			//var ratio = grid.ActualWidth/historyColumn.ActualWidth;
			//var width = new GridLength(window.ActualWidth/3);

			//if (ratio > 3)
			//	historyColumn.Width = width;
		}
	}
}