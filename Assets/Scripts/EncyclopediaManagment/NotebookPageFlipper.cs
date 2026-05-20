using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class NotebookPageFlipper : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("Used to gate input — pages only flip while the notebook is open.")]
    public NoteMenu noteMenu;

    [Header("Sheets")]
    [Tooltip("Each Sheet is a GameObject whose pivot sits at the spine. " +
             "The sheet's FrontContent is the right page of spread N; its BackContent (rotated 180° on Y) is the left page of spread N+1. " +
             "Order matters: index 0 is the first sheet to flip.")]
    public List<Transform> sheets = new List<Transform>();

    [Header("Flip Geometry")]
    [Tooltip("Local Euler angles a sheet has when unflipped (resting on the right side).")]
    public Vector3 unflippedEuler = Vector3.zero;
    [Tooltip("Local Euler angles a sheet has when flipped (resting on the left side). Usually (0,-180,0).")]
    public Vector3 flippedEuler = new Vector3(0f, -180f, 0f);

    [Header("Flip Timing")]
    public float flipDuration = 0.6f;
    public AnimationCurve flipCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Stacking")]
    [Tooltip("Tiny vertical offset per sheet to prevent z-fighting between stacked pages. Set 0 if your pages have real thickness.")]
    public float stackOffset = 0.0005f;
    [Tooltip("Local up axis of the book interior (which direction sheets stack).")]
    public Vector3 stackAxis = Vector3.up;

    [Header("Input")]
    public bool useKeyboard = true;
    [Tooltip("Optional UI buttons can call FlipNext/FlipPrev directly.")]
    public bool wrapAround = false;

    private int topUnflippedIndex = 0; // index of the next sheet that would flip forward
    private bool isFlipping = false;

    public int CurrentSpread => topUnflippedIndex;
    public int SpreadCount => sheets.Count + 1;
    public bool IsFlipping => isFlipping;

    void Start()
    {
        ResetSheets();
    }

    void Update()
    {
        if (!useKeyboard) return;
        if (noteMenu == null || !noteMenu.IsNoteOpen) return;
        if (isFlipping) return;
        if (Keyboard.current == null) return;

        bool next = Keyboard.current.rightArrowKey.wasPressedThisFrame
                 || Keyboard.current.eKey.wasPressedThisFrame;
        bool prev = Keyboard.current.leftArrowKey.wasPressedThisFrame
                 || Keyboard.current.qKey.wasPressedThisFrame;

        if (next) FlipNext();
        else if (prev) FlipPrev();
    }

    public void FlipNext()
    {
        if (isFlipping) return;
        if (topUnflippedIndex >= sheets.Count)
        {
            if (!wrapAround) return;
            // Wrap: flip every flipped sheet back at once (snap, no animation) before continuing
            ResetSheets();
        }
        Transform sheet = sheets[topUnflippedIndex];
        StartCoroutine(FlipRoutine(sheet, unflippedEuler, flippedEuler, forward: true));
    }

    public void FlipPrev()
    {
        if (isFlipping) return;
        if (topUnflippedIndex <= 0)
        {
            if (!wrapAround) return;
            // Wrap: flip everything forward at once before continuing
            FlipAllForwardSnap();
        }
        Transform sheet = sheets[topUnflippedIndex - 1];
        StartCoroutine(FlipRoutine(sheet, flippedEuler, unflippedEuler, forward: false));
    }

    private IEnumerator FlipRoutine(Transform sheet, Vector3 from, Vector3 to, bool forward)
    {
        isFlipping = true;

        Quaternion qFrom = Quaternion.Euler(from);
        Quaternion qTo = Quaternion.Euler(to);

        float t = 0f;
        while (t < flipDuration)
        {
            t += Time.unscaledDeltaTime;
            float u = flipCurve.Evaluate(Mathf.Clamp01(t / flipDuration));
            sheet.localRotation = Quaternion.Slerp(qFrom, qTo, u);
            yield return null;
        }
        sheet.localRotation = qTo;

        if (forward) topUnflippedIndex++;
        else topUnflippedIndex--;

        RestackOffsets();
        isFlipping = false;
    }

    public void ResetSheets()
    {
        topUnflippedIndex = 0;
        for (int i = 0; i < sheets.Count; i++)
        {
            if (sheets[i] == null) continue;
            sheets[i].localRotation = Quaternion.Euler(unflippedEuler);
        }
        RestackOffsets();
    }

    private void FlipAllForwardSnap()
    {
        for (int i = 0; i < sheets.Count; i++)
        {
            if (sheets[i] == null) continue;
            sheets[i].localRotation = Quaternion.Euler(flippedEuler);
        }
        topUnflippedIndex = sheets.Count;
        RestackOffsets();
    }

    private void RestackOffsets()
    {
        // Unflipped sheets stack on the right side, top of pile = lowest index that's unflipped.
        // Flipped sheets stack on the left side, top of pile = highest index that's flipped.
        // We keep each sheet's local position at zero except for a tiny offset along stackAxis
        // so the topmost visible sheet on each side gets the highest offset (rendered above).
        Vector3 axis = stackAxis.normalized;

        // Right pile: indices [topUnflippedIndex .. sheets.Count-1]
        //   topUnflipped is on TOP (most visible), so it gets the largest offset.
        int rightPileTop = topUnflippedIndex;
        for (int i = topUnflippedIndex; i < sheets.Count; i++)
        {
            if (sheets[i] == null) continue;
            int depth = i - rightPileTop; // 0 = top
            sheets[i].localPosition = -axis * stackOffset * depth;
        }

        // Left pile: indices [0 .. topUnflippedIndex-1]
        //   The most recently flipped (topUnflippedIndex - 1) is on TOP.
        int leftPileTop = topUnflippedIndex - 1;
        for (int i = 0; i <= leftPileTop; i++)
        {
            if (sheets[i] == null) continue;
            int depth = leftPileTop - i;
            sheets[i].localPosition = -axis * stackOffset * depth;
        }
    }
}
