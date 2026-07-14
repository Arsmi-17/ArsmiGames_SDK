using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArsmiGames.Demo
{
    /// <summary>
    /// A kids' quiz. The quiz is not the point — the save is.
    ///
    /// Progress goes through <see cref="ArsmiSave"/>, so the three save-mode buttons
    /// exercise all three published options against identical game code. The real test is
    /// the boring one: answer a few questions, reload the page, and see whether you are
    /// still where you left off.
    ///
    /// It also spends a rewarded ad on a hint, which is the honest shape of that feature:
    /// the ad is a platform overlay, and only the platform decides whether it was watched.
    /// </summary>
    public class KidsQuiz : MonoBehaviour
    {
        private struct Question
        {
            public string Prompt;
            public string[] Options;
            public int Answer;
            public string Hint;

            public Question(string prompt, int answer, string hint, params string[] options)
            {
                Prompt = prompt;
                Answer = answer;
                Hint = hint;
                Options = options;
            }
        }

        private static readonly Question[] Questions =
        {
            new Question("What colour do you get when you mix red and yellow?", 1, "Think of a pumpkin.",
                         "Green", "Orange", "Purple", "Blue"),
            new Question("How many legs does a spider have?", 2, "Two more than an insect.",
                         "6", "4", "8", "10"),
            new Question("Which animal says \"moo\"?", 0, "It gives us milk.",
                         "Cow", "Duck", "Sheep", "Horse"),
            new Question("What is 7 + 5?", 3, "One more than eleven.",
                         "10", "13", "11", "12"),
            new Question("Which planet do we live on?", 1, "You are standing on it.",
                         "Mars", "Earth", "Jupiter", "Venus"),
            new Question("How many days are there in a week?", 2, "Monday through Sunday.",
                         "5", "6", "7", "8"),
            new Question("What do bees make?", 0, "It is sweet and golden.",
                         "Honey", "Milk", "Bread", "Jam"),
            new Question("Which one is a fruit?", 3, "Monkeys love it.",
                         "Carrot", "Potato", "Onion", "Banana"),
            new Question("What shape has three sides?", 1, "Like a slice of pizza.",
                         "Square", "Triangle", "Circle", "Hexagon"),
            new Question("Which is the biggest?", 2, "It has a trunk.",
                         "Cat", "Mouse", "Elephant", "Rabbit"),
        };

        private const string KeyIndex = "quiz_index";
        private const string KeyScore = "quiz_score";
        private const string KeyBest = "quiz_best";

        private ArsmiSave _save;
        private SdkFunctionPanel _console;

        private TextMeshProUGUI _modeNote;
        private TextMeshProUGUI _scoreLine;
        private TextMeshProUGUI _prompt;
        private TextMeshProUGUI _feedback;
        private readonly List<Button> _modeButtons = new List<Button>();
        private readonly List<Button> _optionButtons = new List<Button>();
        private readonly List<TextMeshProUGUI> _optionLabels = new List<TextMeshProUGUI>();
        private Button _hintButton;

        private int _index;
        private int _score;
        private int _best;
        private bool _answered;
        private bool _hintShown;

        public void Build(Transform parent, ArsmiSave save, SdkFunctionPanel console)
        {
            _save = save;
            _console = console;

            // The card fills the column; a scroll inside it means a narrow frame scrolls
            // rather than clipping the buttons off the bottom.
            var host = DemoUI.Node("QuizCard", parent);
            DemoUI.Box(host, DemoUI.Panel);
            DemoUI.Fill(host);
            var content = DemoUI.Scroll(host.transform, out _, 18);

            DemoUI.Section(content, "Save mode");

            var modeRow = DemoUI.Row(content, 46);
            _modeButtons.Add(DemoUI.Btn(modeRow.transform, "Local only", () => SetMode(SaveTarget.LocalOnly), null, null, 46, 16));
            _modeButtons.Add(DemoUI.Btn(modeRow.transform, "Platform", () => SetMode(SaveTarget.PlatformMirror), null, null, 46, 16));
            _modeButtons.Add(DemoUI.Btn(modeRow.transform, "Own backend", () => SetMode(SaveTarget.OwnBackend), null, null, 46, 16));

            _modeNote = DemoUI.Label(content, "", 15, DemoUI.Muted);
            DemoUI.Height(_modeNote.gameObject, 66);

            DemoUI.Section(content, "Quiz");

            _scoreLine = DemoUI.Label(content, "", 17, DemoUI.Text, TextAlignmentOptions.Left, FontStyles.Bold);
            DemoUI.Height(_scoreLine.gameObject, 26);

            _prompt = DemoUI.Label(content, "", 26, DemoUI.Text, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            DemoUI.Height(_prompt.gameObject, 84);

            for (var i = 0; i < 4; i++)
            {
                var choice = i; // capture per iteration, or every button answers with 3
                var button = DemoUI.Btn(content, "", () => Answer(choice), DemoUI.PanelSoft, null, 56, 19);
                _optionButtons.Add(button);
                _optionLabels.Add(button.GetComponentInChildren<TextMeshProUGUI>());
            }

            _feedback = DemoUI.Label(content, "", 17, DemoUI.Muted, TextAlignmentOptions.Left, FontStyles.Bold);
            DemoUI.Height(_feedback.gameObject, 30);

            // The ad is drawn by the platform, over the game. The game only asks.
            _hintButton = DemoUI.Btn(content, "Watch an ad for a hint", ShowAdForHint,
                                     new Color(DemoUI.Gold.r, DemoUI.Gold.g, DemoUI.Gold.b, 0.16f), DemoUI.Gold, 52, 18);

            var actions = DemoUI.Row(content, 52);
            DemoUI.Btn(actions.transform, "Next question", Next, DemoUI.AccentSoft, DemoUI.Accent);
            DemoUI.Btn(actions.transform, "Reset progress", ResetProgress,
                       new Color(DemoUI.Bad.r, DemoUI.Bad.g, DemoUI.Bad.b, 0.16f), DemoUI.Bad);

            _save.OnExternalChange += ReloadFromSave;
            _save.OnError += message => _console?.Log($"save error: {message}");

            var hub = GameHubBridge.Instance;
            if (hub != null)
            {
                hub.OnAdStarted += () => _console?.Log("ad started — platform overlay is up");
                hub.OnAdFinished += OnAdFinished;
            }

            SetMode(SaveTarget.PlatformMirror);
        }

        private void SetMode(SaveTarget target)
        {
            var backend = DemoBootstrap.Instance != null ? DemoBootstrap.Instance.Backend : null;
            _save.Configure(target, backend);

            switch (target)
            {
                case SaveTarget.LocalOnly:
                    _modeNote.text = "<b>Local only.</b> PlayerPrefs. Clear site data and it is gone, and it never reaches another device.";
                    break;
                case SaveTarget.PlatformMirror:
                    _modeNote.text = "<b>Platform.</b> Saved locally and mirrored to your account. Play as a guest, sign in, and the progress comes with you.";
                    break;
                case SaveTarget.OwnBackend:
                    _modeNote.text = backend != null && backend.IsConfigured
                        ? "<b>Own backend.</b> Arsmi Games stores nothing — it only says who you are. Rows are keyed on the per-game playerId."
                        : "<b>Own backend.</b> <color=#e65b5b>Not configured</color> — set the Supabase URL and anon key on the ArsmiDemo object.";
                    break;
            }

            // Highlight the active mode.
            for (var i = 0; i < _modeButtons.Count; i++)
            {
                var image = _modeButtons[i].GetComponent<Image>();
                var active = (int)target == i;
                if (image != null) image.color = active ? DemoUI.AccentSoft : DemoUI.PanelSoft;
                var label = _modeButtons[i].GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.color = active ? DemoUI.Accent : DemoUI.Muted;
            }

            _console?.Log($"save mode → {target}");
            ReloadFromSave();
        }

        private void ReloadFromSave()
        {
            _index = Mathf.Clamp(_save.GetInt(KeyIndex, 0), 0, Questions.Length - 1);
            _score = _save.GetInt(KeyScore, 0);
            _best = _save.GetInt(KeyBest, 0);
            _answered = false;
            _hintShown = false;
            _feedback.text = "";
            Render();
        }

        private void Render()
        {
            var question = Questions[_index];

            _scoreLine.text =
                $"Question {_index + 1} / {Questions.Length}     " +
                $"Score <color=#ecebf1>{_score}</color>     Best <color=#cc785c>{_best}</color>";
            _prompt.text = question.Prompt;

            for (var i = 0; i < _optionButtons.Count; i++)
            {
                var has = i < question.Options.Length;
                _optionButtons[i].gameObject.SetActive(has);
                if (!has) continue;

                _optionLabels[i].text = question.Options[i];
                _optionLabels[i].color = DemoUI.Text;
                _optionButtons[i].GetComponent<Image>().color = DemoUI.PanelSoft;
                _optionButtons[i].interactable = !_answered;
            }

            if (_hintButton != null) _hintButton.interactable = !_answered && !_hintShown;
        }

        private void ShowAdForHint()
        {
            var hub = GameHubBridge.Instance;
            if (hub == null) return;

            _console?.Log("→ ad:show (rewarded)");
            // Pause and mute yourself. The platform covers the frame, but it will not stop
            // the game for you, and a game still playing under an ad sounds broken.
            hub.SetMuted(true);
            hub.ShowRewardedAd("quiz-hint");
        }

        private void OnAdFinished(bool rewarded, int balance)
        {
            var hub = GameHubBridge.Instance;
            hub?.SetMuted(false);

            _console?.Log(rewarded
                ? $"← ad finished: rewarded (balance {balance})"
                : "← ad finished: skipped or failed — no hint");

            if (!rewarded)
            {
                _feedback.text = "<color=#9a99a8>No hint — the ad was not watched.</color>";
                _feedback.color = DemoUI.Muted;
                return;
            }

            _hintShown = true;
            _feedback.text = $"<color=#e8b339>Hint:</color> {Questions[_index].Hint}";
            _feedback.color = DemoUI.Gold;
            if (_hintButton != null) _hintButton.interactable = false;
        }

        private void Answer(int choice)
        {
            if (_answered) return;
            _answered = true;

            var question = Questions[_index];
            var correct = choice == question.Answer;

            _optionButtons[choice].GetComponent<Image>().color = correct
                ? new Color(DemoUI.Good.r, DemoUI.Good.g, DemoUI.Good.b, 0.18f)
                : new Color(DemoUI.Bad.r, DemoUI.Bad.g, DemoUI.Bad.b, 0.18f);
            _optionLabels[choice].color = correct ? DemoUI.Good : DemoUI.Bad;
            _optionLabels[question.Answer].color = DemoUI.Good;

            foreach (var button in _optionButtons) button.interactable = false;
            if (_hintButton != null) _hintButton.interactable = false;

            if (correct)
            {
                _score++;
                _feedback.text = "<color=#5cc47d>Correct!</color>";
                _feedback.color = DemoUI.Good;

                // Achievements and leaderboards are server-authoritative and go through their
                // own APIs — deliberately not through the save, which the player can edit.
                GameHubBridge.Instance?.AchievementProgress("quiz_correct", 1);
            }
            else
            {
                _feedback.text = $"<color=#e65b5b>Not quite.</color> The answer is {question.Options[question.Answer]}.";
                _feedback.color = DemoUI.Bad;
            }

            if (_score > _best)
            {
                _best = _score;
                _save.SetInt(KeyBest, _best);
                GameHubBridge.Instance?.LeaderboardScore(_best, "quiz_score", "Quiz score");
            }

            _save.SetInt(KeyScore, _score);
            Render();
        }

        private void Next()
        {
            _index = (_index + 1) % Questions.Length;
            if (_index == 0) _score = 0; // wrapped around: fresh run

            _save.SetInt(KeyIndex, _index);
            _save.SetInt(KeyScore, _score);

            // The player could close the tab on the very next frame. The SDK forces a write
            // when the page hides, but asking here costs nothing and makes the
            // "answer, reload, still there" test deterministic.
            _save.Flush();

            _answered = false;
            _hintShown = false;
            _feedback.text = "";
            Render();
        }

        private void ResetProgress()
        {
            _index = 0;
            _score = 0;
            _best = 0;
            _save.ClearAll();
            _save.SetInt(KeyIndex, 0);
            _save.SetInt(KeyScore, 0);
            _save.SetInt(KeyBest, 0);
            _save.Flush();

            _answered = false;
            _hintShown = false;
            _feedback.text = "";
            Render();
            _console?.Log("progress reset");
        }
    }
}
