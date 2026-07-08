using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BunchOfButtonsLib;
using UnityEngine;
using Rnd = UnityEngine.Random;

public class SapphireButtonScript : MonoBehaviour
{
    public KMBombModule Module;
    public KMBombInfo BombInfo;
    public KMAudio Audio;
    public KMSelectable ButtonSelectable;
    public GameObject ButtonCap;

    public Transform BitmapsParent;
    public Transform WordsParent;
    public Transform ResetParent;

    // Objects for instantiating/animating
    public MaskShaderManager MaskShaderManager;
    public MeshRenderer Mask;
    public TextMesh[] WordTexts;
    public TextMesh WordResultText;
    public TextMesh ResetText;
    public Mesh Quad;
    public Texture PixelTexture;

    // Solving process
    private SapphireButtonPuzzle _puzzle;
    private int _bitmapIx;
    private Stage _stage;
    private int _numTaps;
    private Coroutine _numTapTimeout;
    private int _wordHighlight;
    private int _wordSection;
    private int _wordProgress;

    // Internals
    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private Coroutine _pressHandler;
    private MaskMaterials _maskMaterials;

    enum Stage
    {
        Bitmaps,
        Word,
        Reset,
        Solved
    }

    private void Start()
    {
        _moduleId = _moduleIdCounter++;
        ButtonSelectable.OnInteract += ButtonPress;
        ButtonSelectable.OnInteractEnded += ButtonRelease;

        _maskMaterials = MaskShaderManager.MakeMaterials();
        _maskMaterials.Text.mainTexture = WordResultText.GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        _maskMaterials.DiffuseText.mainTexture = WordResultText.GetComponent<MeshRenderer>().sharedMaterial.mainTexture;
        Mask.sharedMaterial = _maskMaterials.Mask;

        GeneratePuzzle();
        _stage = Stage.Bitmaps;

        StartCoroutine(AnimationManager(Stage.Bitmaps, BitmapsParent, AnimateBitmaps));
        StartCoroutine(AnimationManager(new[] { Stage.Word, Stage.Solved }, WordsParent, AnimateWordsAndSolve));
        StartCoroutine(AnimationManager(Stage.Reset, ResetParent, AnimateReset));
    }

    private void GeneratePuzzle()
    {
        var seed = Rnd.Range(0, int.MaxValue);
        Debug.LogFormat("<The Sapphire Button #{0}> Seed: {1}", _moduleId, seed);
        _puzzle = SapphireButtonPuzzle.GeneratePuzzle(seed);
        foreach (var line in _puzzle.Logging.Split('\n'))
            Debug.LogFormat(@"[The Sapphire Button #{0}] {1}", _moduleId, line);
        Debug.LogFormat(@"[The Sapphire Button #{0}] Solution: {1}", _moduleId, _puzzle.Answer);
    }

    private bool ButtonPress()
    {
        StartCoroutine(AnimateButton(0f, -0.05f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonPress, transform);
        if (_stage != Stage.Solved)
            _pressHandler = StartCoroutine(HandlePress());
        return false;
    }

    private void ButtonRelease()
    {
        StartCoroutine(AnimateButton(-0.05f, 0f));
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.BigButtonRelease, transform);
        if (_pressHandler != null)
            StopCoroutine(_pressHandler);

        switch (_stage)
        {
            case Stage.Bitmaps:
                if (_numTapTimeout != null)
                    StopCoroutine(_numTapTimeout);
                _numTaps++;
                _numTapTimeout = StartCoroutine(NumberTimeout());
                break;

            case Stage.Word:
                if (_wordSection == 0)
                {
                    if (_wordHighlight != (_puzzle.Answer[_wordProgress] - 'A') / 9)
                    {
                        Debug.LogFormat(@"[The Sapphire Button #{0}] Stage 2: You selected section {1} for letter #{2}. Strike!", _moduleId, WordTexts[_wordHighlight].text, _wordProgress + 1);
                        Module.HandleStrike();
                    }
                    else
                        _wordSection = _wordHighlight + 1;
                }
                else if (_wordSection <= 3)
                {
                    if (_wordHighlight != ((_puzzle.Answer[_wordProgress] - 'A') % 9) / 3)
                    {
                        Debug.LogFormat(@"[The Sapphire Button #{0}] Stage 2: You selected section {1} for letter #{2}. Strike!", _moduleId, WordTexts[_wordHighlight].text, _wordProgress + 1);
                        Module.HandleStrike();
                        _wordSection = 0;
                    }
                    else
                        _wordSection = (_wordSection - 1) * 3 + _wordHighlight + 4;
                }
                else if (_wordSection + _wordHighlight >= 30)
                {
                    Debug.LogFormat(@"[The Sapphire Button #{0}] Stage 2: You submitted the empty slot after Z. Strike!", _moduleId);
                    Module.HandleStrike();
                    _wordSection = 0;
                }
                else
                {
                    var nextLetter = (char) ('A' + ((_wordSection - 4) * 3 + _wordHighlight));
                    if (nextLetter != _puzzle.Answer[_wordProgress])
                    {
                        Debug.LogFormat(@"[The Sapphire Button #{0}] Stage 2: You submitted {1} for letter #{2}. Strike!", _moduleId, nextLetter, _wordProgress + 1);
                        Module.HandleStrike();
                    }
                    else
                    {
                        _wordProgress++;
                        if (_wordProgress == _puzzle.Answer.Length)
                        {
                            Module.HandlePass();
                            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
                            _stage = Stage.Solved;
                        }
                    }
                    _wordSection = 0;
                }
                break;

            case Stage.Reset:
                _stage = Stage.Bitmaps;
                break;
        }
    }

    private IEnumerator NumberTimeout()
    {
        yield return new WaitForSeconds(1.5f);

        if (_numTaps == 4)
            _stage = Stage.Word;
        else if (_numTaps < 4)
        {
            _stage = Stage.Bitmaps;
            _bitmapIx = _numTaps - 1;
        }
        else
        {
            Debug.LogFormat(@"[The Sapphire Button #{0}] You tapped the button {1} times. Only 1–4 times is allowed. Strike!", _moduleId, _numTaps);
            Module.HandleStrike();
        }
        _numTaps = 0;
    }

    private IEnumerator HandlePress()
    {
        yield return new WaitForSeconds(.5f);
        Audio.PlaySoundAtTransform("BlueButtonSwoosh", transform);
        _stage = Stage.Reset;
        _numTaps = 0;
        if (_numTapTimeout != null)
            StopCoroutine(_numTapTimeout);
    }

    private IEnumerator AnimationManager(Stage requiredStage, Transform parent, Func<Func<bool>, IEnumerator> gen, Light spotlight = null)
    {
        return AnimationManager(new[] { requiredStage }, parent, gen, spotlight);
    }

    private IEnumerator AnimationManager(Stage[] requiredStages, Transform parent, Func<Func<bool>, IEnumerator> gen, Light spotlight = null)
    {
        while (true)
        {
            parent.gameObject.SetActive(false);
            if (spotlight != null)
                spotlight.gameObject.SetActive(false);

            while (!requiredStages.Contains(_stage))
            {
                if (_stage == Stage.Solved)
                    yield break;
                yield return null;
            }

            var stop = false;
            StartCoroutine(gen(() => stop));
            yield return new WaitForSeconds(1f);
            parent.localPosition = new Vector3(0, 0, -.2f);
            parent.gameObject.SetActive(true);
            if (spotlight != null)
            {
                spotlight.intensity = 0;
                spotlight.gameObject.SetActive(true);
            }
            yield return Animation(.63f, t =>
            {
                parent.localPosition = new Vector3(0, 0, Easing.BackOut(t, -.2f, 0, 1));
                if (spotlight != null)
                    spotlight.intensity = 5 * t;
            });

            while (requiredStages.Contains(_stage))
            {
                if (_stage == Stage.Solved)
                    yield break;
                yield return null;
            }

            yield return Animation(.63f, t =>
            {
                parent.localPosition = new Vector3(0, 0, Easing.BackIn(t, 0, .2f, 1));
                if (spotlight != null)
                    spotlight.intensity = 5 * (1 - t);
            });

            stop = true;
        }
    }

    private IEnumerator Animation(float duration, Action<float> action, bool fullTime = false)
    {
        var elapsed = 0f;
        while (elapsed < duration)
        {
            action(fullTime ? elapsed : elapsed / duration);
            yield return null;
            elapsed += Time.deltaTime;
        }
        action(fullTime ? duration : 1);
    }

    private IEnumerator AnimateButton(float a, float b)
    {
        var duration = 0.1f;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            ButtonCap.transform.localPosition = new Vector3(0f, Easing.InOutQuad(elapsed, a, b, duration), 0f);
            yield return null;
            elapsed += Time.deltaTime;
        }
        ButtonCap.transform.localPosition = new Vector3(0f, b, 0f);
    }

    private GameObject MakeGameObject(string name, Transform parent, float scale, Vector3? position = null, Quaternion? rotation = null)
    {
        return MakeGameObject(name, parent, position, rotation, scale: new Vector3(scale, scale, scale));
    }
    private GameObject MakeGameObject(string name, Transform parent, Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null)
    {
        var obj = new GameObject(name);
        obj.transform.parent = parent;
        obj.transform.localPosition = position ?? new Vector3(0, 0, 0);
        obj.transform.localRotation = rotation ?? Quaternion.identity;
        obj.transform.localScale = scale ?? new Vector3(1, 1, 1);
        return obj;
    }

    private IEnumerator AnimateBitmaps(Func<bool> stop)
    {
        var objects = new List<GameObject>();
        for (var cell = 0; cell < _puzzle.Cells; cell++)
        {
            var obj = MakeGameObject(string.Format("Pixel {0}", cell), BitmapsParent,
                position: new Vector3((-(_puzzle.Width - 1) * .5f + (cell % _puzzle.Width)) * 1.02f, (2f - (cell / _puzzle.Width)) * 1.02f, 2f));
            obj.AddComponent<MeshFilter>().sharedMesh = Quad;
            var mr = obj.AddComponent<MeshRenderer>();
            mr.material = _maskMaterials.DiffuseTint;
            mr.material.mainTexture = PixelTexture;
            objects.Add(obj);
        }

        var bitmapIxPrev = -1;
        while (true)
        {
            while (!stop() && bitmapIxPrev == _bitmapIx)
                yield return null;
            if (stop())
                break;
            var bitmapIxNew = _bitmapIx;

            const float sweepSpeed = .15f;
            const float flipDuration = .7f;
            yield return Animation((_puzzle.Width + 5) * sweepSpeed + flipDuration, fullTime: true, action: t =>
            {
                var axis = new Vector3(1, 1, 0);
                for (var cell = 0; cell < _puzzle.Cells; cell++)
                {
                    var startTime = (cell % _puzzle.Width + (cell / _puzzle.Width) * .7f) * sweepSpeed;
                    objects[cell].transform.localRotation =
                        (t > startTime + flipDuration || xored(bitmapIxPrev, cell)) && (t < startTime || xored(bitmapIxNew, cell)) ? Quaternion.identity :
                        (t > startTime + flipDuration || !xored(bitmapIxPrev, cell)) && (t < startTime || !xored(bitmapIxNew, cell)) ? Quaternion.AngleAxis(180, axis) :
                        Quaternion.AngleAxis(Easing.InOutQuad(t - startTime,
                            _puzzle.FontedXored[bitmapIxNew][cell] ? 180 : 0,
                            _puzzle.FontedXored[bitmapIxNew][cell] ? 0 : 180, flipDuration), axis);
                }
            });

            bitmapIxPrev = bitmapIxNew;
        }
        foreach (var obj in objects)
            Destroy(obj);
    }

    private bool xored(int bitmapIx, int cell)
    {
        return bitmapIx >= 0 && _puzzle.FontedXored[bitmapIx][cell];
    }

    private IEnumerator AnimateWordsAndSolve(Func<bool> stop)
    {
        WordResultText.GetComponent<MeshRenderer>().sharedMaterial = _maskMaterials.DiffuseText;
        for (var i = 0; i < 3; i++)
            WordTexts[i].GetComponent<MeshRenderer>().sharedMaterial = _maskMaterials.DiffuseText;

        while (!stop())
        {
            if (_stage == Stage.Solved)
            {
                WordResultText.text = _puzzle.Answer;

                for (var i = 0; i < 3; i++)
                    WordTexts[i].gameObject.SetActive(false);

                yield return Animation(2.6f, t =>
                {
                    WordResultText.transform.localPosition = Vector3.Lerp(new Vector3(0, -.02f, -.03f), new Vector3(0, 0, 0), Easing.InOutQuad(t, 0, 1, 1));
                    WordResultText.transform.localScale = Vector3.Lerp(new Vector3(.015f, .015f, .015f), new Vector3(.025f, .025f, .025f), Easing.InOutQuad(t, 0, 1, 1));
                    WordResultText.color = Color.Lerp(new Color32(0xE1, 0xE1, 0xE1, 0xFF), new Color32(0x0D, 0xE1, 0x0F, 0xFF), Easing.InOutQuad(t, 0, 1, 1));
                });
                yield break;
            }
            else
            {
                _wordHighlight = (int) ((Time.time % 1.8f) / 1.8f * 3);
                for (var i = 0; i < 3; i++)
                {
                    WordTexts[i].gameObject.SetActive(true);
                    WordTexts[i].color = i == _wordHighlight ? Color.white : (Color) new Color32(0x3D, 0x69, 0xC7, 0xFF);
                }
                WordResultText.text = _puzzle.Answer.Substring(0, _wordProgress) + "_";

                if (_wordSection == 0)
                {
                    WordTexts[0].text = "A–I";
                    WordTexts[1].text = "J–R";
                    WordTexts[2].text = "S–Z";
                }
                else if (_wordSection <= 3)
                {
                    for (var triplet = 0; triplet < 3; triplet++)
                        WordTexts[triplet].text = _wordSection == 3 && triplet == 2 ? "YZ" : Enumerable.Range(0, 3).Select(ltr => (char) ('A' + (_wordSection - 1) * 9 + 3 * triplet + ltr)).Join("");
                }
                else
                {
                    for (var ltr = 0; ltr < 3; ltr++)
                        WordTexts[ltr].text = _wordSection == 12 && ltr == 2 ? "" : ((char) ('A' + ((_wordSection - 4) * 3 + ltr))).ToString();
                }

                yield return null;
            }
        }

        for (var i = 0; i < 3; i++)
            WordTexts[i].gameObject.SetActive(false);
    }

    private IEnumerator AnimateReset(Func<bool> stop)
    {
        ResetText.GetComponent<MeshRenderer>().sharedMaterial = _maskMaterials.DiffuseText;
        ResetText.gameObject.SetActive(true);
        while (!stop())
            yield return null;
        ResetText.gameObject.SetActive(false);
    }


#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "!{0} tap 3 [stage 1: tap 3 times] | !{0} tap 1 3 2 3 1 [stage 2: tap when the highlight is in these positions] | !{0} reset";
#pragma warning restore 0414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        if (Regex.IsMatch(command, @"^\s*reset\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            yield return null;
            ButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.75f);
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            yield break;
        }

        Match m;
        if (_stage == Stage.Bitmaps && (m = Regex.Match(command, @"^\s*tap\s+([1234])\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            yield return null;
            var des = m.Groups[1].Value[0] - '0';
            for (var i = 0; i < des; i++)
            {
                ButtonSelectable.OnInteract();
                yield return new WaitForSeconds(.1f);
                ButtonSelectable.OnInteractEnded();
                yield return new WaitForSeconds(.1f);
            }
            yield break;
        }

        if (_stage == Stage.Word && (m = Regex.Match(command, @"^\s*tap\s+([,; 123]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).Success)
        {
            yield return null;
            foreach (var ch in m.Groups[1].Value)
            {
                if (ch < '1' || ch > '3')
                    continue;
                while (_wordHighlight != ch - '1')
                    yield return null;
                ButtonSelectable.OnInteract();
                ButtonSelectable.OnInteractEnded();
                yield return new WaitForSeconds(.2f);
            }
            yield break;
        }
    }

    public IEnumerator TwitchHandleForcedSolve()
    {
        if (_stage == Stage.Bitmaps)
        {
            ButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            ButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            ButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.1f);
            ButtonSelectable.OnInteract();
            yield return new WaitForSeconds(.1f);
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(1.5f);
        }

        while (_stage == Stage.Word)
        {
            var ltr = _puzzle.Answer[_wordProgress] - 'A';
            var requiredHighlight =
                _wordSection == 0 ? ltr / 9 :
                _wordSection <= 3 ? (ltr / 3) % 3 : ltr % 3;

            while (_wordHighlight != requiredHighlight)
                yield return true;
            ButtonSelectable.OnInteract();
            ButtonSelectable.OnInteractEnded();
            yield return new WaitForSeconds(.2f);
        }
    }
}
