using System;
using System.Threading.Tasks;
using CapstoneGame.Auth;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The sign-in / create-account screen. Put it on an empty GameObject in the Login scene; it builds its own
/// UI when the scene starts, so there is nothing to wire up in the Inspector.
///
/// On start it tries to restore the saved session. Signed-in players see "Play" and "Sign out";
/// everyone else sees the email/password form plus "Play as guest".
/// </summary>
public class LoginScreen : MonoBehaviour
{
    [Tooltip("Scene loaded by the Play and Play as guest buttons. It must be in File > Build Profiles.")]
    [SerializeField] private string gameSceneName = "SampleScene";

    // Sizes are in reference pixels for a 1080 x 1920 portrait screen (see CanvasScaler below).
    private const float RowHeight = 180f;
    private const float ColumnWidth = 900f;
    private const int FontSize = 54;

    private static readonly Color Background = new Color(0.043f, 0.051f, 0.102f, 1f);
    private static readonly Color FieldColor = new Color(0.16f, 0.18f, 0.28f, 1f);
    private static readonly Color PrimaryColor = new Color(0.20f, 0.55f, 0.95f, 1f);
    private static readonly Color SecondaryColor = new Color(0.28f, 0.30f, 0.42f, 1f);
    private static readonly Color ErrorColor = new Color(1f, 0.45f, 0.45f, 1f);
    private static readonly Color InfoColor = new Color(0.85f, 0.88f, 1f, 1f);

    private SupabaseAuth auth;
    private Font font;

    public InputField EmailField { get; private set; }
    public InputField PasswordField { get; private set; }
    public Button SignInButton { get; private set; }
    public Button SignUpButton { get; private set; }
    public Button GuestButton { get; private set; }
    public Button PlayButton { get; private set; }
    public Button SignOutButton { get; private set; }
    public Text StatusText { get; private set; }
    public Text SignedInLabel { get; private set; }
    public GameObject FormPanel { get; private set; }
    public GameObject SignedInPanel { get; private set; }

    /// <summary>True while a request to Supabase is in progress.</summary>
    public bool Busy { get; private set; }

    private void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUi();
    }

    private void Start()
    {
        auth = AuthServices.Client;
        auth.SessionChanged += OnSessionChanged;

        if (!auth.IsConfigured)
        {
            ShowStatus(SupabaseAuth.NotConfiguredMessage +
                       (Application.isEditor ? " (Tools > Supabase > Create or Select Secrets File)" : " You can still play as a guest."),
                isError: false);
            ShowPanels();
            return;
        }

        if (auth.IsSignedIn) ShowPanels();
        else Run(auth.RestoreSession, "Checking saved login...");
    }

    private void OnDestroy()
    {
        if (auth != null) auth.SessionChanged -= OnSessionChanged;
    }

    public void SignIn() => Run(() => auth.SignIn(EmailField.text, PasswordField.text), "Signing in...");

    public void SignUp() => Run(() => auth.SignUp(EmailField.text, PasswordField.text), "Creating account...");

    public void SignOut() => Run(auth.SignOut, "Signing out...");

    public void Play() => SceneManager.LoadScene(gameSceneName);

    private async void Run(Func<Task<AuthResult>> action, string busyMessage)
    {
        if (Busy) return;
        SetBusy(true);
        ShowStatus(busyMessage, isError: false);

        AuthResult result;
        try
        {
            result = await action();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            result = AuthResult.Fail("Something went wrong. Please try again.");
        }

        if (this == null) return; // the scene was unloaded while we waited
        SetBusy(false);

        ShowStatus(result.Message, isError: result.Status == AuthStatus.Failed);

        if (result.Succeeded) PasswordField.text = "";
        ShowPanels();
    }

    private void OnSessionChanged(AuthSession session)
    {
        if (this != null) ShowPanels();
    }

    private void ShowPanels()
    {
        bool signedIn = auth != null && auth.IsSignedIn;
        FormPanel.SetActive(!signedIn);
        SignedInPanel.SetActive(signedIn);
        if (signedIn) SignedInLabel.text = $"Signed in as\n{auth.Session.Email}";
        SetBusy(Busy);
    }

    private void SetBusy(bool busy)
    {
        Busy = busy;
        bool online = auth != null && auth.IsConfigured;
        SignInButton.interactable = !busy && online;
        SignUpButton.interactable = !busy && online;
        SignOutButton.interactable = !busy;
        GuestButton.interactable = !busy;
        PlayButton.interactable = !busy;
        EmailField.interactable = !busy && online;
        PasswordField.interactable = !busy && online;
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.text = message ?? "";
        StatusText.color = isError ? ErrorColor : InfoColor;
    }

    // ---------- UI construction ----------

    private void BuildUi()
    {
        EnsureEventSystem();

        var canvasGo = new GameObject("Login Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // whole layout always fits on screen

        RectTransform background = NewRect("Background", canvasGo.transform);
        Stretch(background);
        background.gameObject.AddComponent<Image>().color = Background;

        RectTransform safe = NewRect("Safe Area", canvasGo.transform);
        Stretch(safe);
        safe.gameObject.AddComponent<SafeArea>();

        RectTransform column = NewRect("Column", safe);
        column.anchorMin = column.anchorMax = column.pivot = new Vector2(0.5f, 0.5f);
        column.sizeDelta = new Vector2(ColumnWidth, 0f);
        var layout = column.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 30f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Text title = NewText("Title", column, "VOID BARRAGE", FontSize * 2, FontStyle.Bold, InfoColor);
        SetHeight(title.gameObject, RowHeight * 1.2f);

        FormPanel = NewPanel("Sign In Form", column);
        EmailField = NewInputField("Email", FormPanel.transform, "Email", InputField.ContentType.EmailAddress);
        PasswordField = NewInputField("Password", FormPanel.transform, "Password", InputField.ContentType.Password);
        SignInButton = NewButton("Sign In Button", FormPanel.transform, "Sign in", PrimaryColor, SignIn);
        SignUpButton = NewButton("Create Account Button", FormPanel.transform, "Create account", SecondaryColor, SignUp);
        GuestButton = NewButton("Guest Button", FormPanel.transform, "Play as guest", SecondaryColor, Play);

        SignedInPanel = NewPanel("Signed In", column);
        SignedInLabel = NewText("Signed In Label", SignedInPanel.transform, "", FontSize, FontStyle.Normal, InfoColor);
        SetHeight(SignedInLabel.gameObject, RowHeight);
        PlayButton = NewButton("Play Button", SignedInPanel.transform, "Play", PrimaryColor, Play);
        SignOutButton = NewButton("Sign Out Button", SignedInPanel.transform, "Sign out", SecondaryColor, SignOut);
        SignedInPanel.SetActive(false);

        StatusText = NewText("Status", column, "", (int)(FontSize * 0.8f), FontStyle.Normal, InfoColor);
        SetHeight(StatusText.gameObject, RowHeight);
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        // This project uses the Input System package only, so UI needs its input module (not StandaloneInputModule).
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void SetHeight(GameObject go, float height)
    {
        if (!go.TryGetComponent(out LayoutElement element)) element = go.AddComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = height;
    }

    private GameObject NewPanel(string name, Transform parent)
    {
        RectTransform rect = NewRect(name, parent);
        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 30f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return rect.gameObject;
    }

    private Text NewText(string name, Transform parent, string text, int size, FontStyle style, Color color)
    {
        RectTransform rect = NewRect(name, parent);
        var label = rect.gameObject.AddComponent<Text>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        return label;
    }

    private InputField NewInputField(string name, Transform parent, string placeholder, InputField.ContentType type)
    {
        RectTransform rect = NewRect(name, parent);
        SetHeight(rect.gameObject, RowHeight);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = FieldColor;

        Text placeholderText = NewText("Placeholder", rect, placeholder, FontSize, FontStyle.Italic, new Color(1f, 1f, 1f, 0.4f));
        Text valueText = NewText("Text", rect, "", FontSize, FontStyle.Normal, Color.white);
        foreach (Text t in new[] { placeholderText, valueText })
        {
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.supportRichText = false;
            var r = t.rectTransform;
            Stretch(r);
            r.offsetMin = new Vector2(40f, 0f);
            r.offsetMax = new Vector2(-40f, 0f);
        }

        var field = rect.gameObject.AddComponent<InputField>();
        field.targetGraphic = image;
        field.textComponent = valueText;
        field.placeholder = placeholderText;
        field.contentType = type;
        field.lineType = InputField.LineType.SingleLine;
        field.characterLimit = 254;
        return field;
    }

    private Button NewButton(string name, Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = NewRect(name, parent);
        SetHeight(rect.gameObject, RowHeight);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        Text text = NewText("Label", rect, label, FontSize, FontStyle.Bold, Color.white);
        Stretch(text.rectTransform);
        return button;
    }
}
