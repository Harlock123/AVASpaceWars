#!/usr/bin/env python3
"""Synthesises AVASpaceWars's sound effects and tunes into ../src/AVASpaceWars/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASpaceWars", "Sounds")
RNG = random.Random(1977)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The ships.
    write("Fire.wav", mix(tone(0.16, 1500, 300, "square", release=1.4), tone(0.1, 2600, 700, "tri", release=1.4, gain=0.4)), 0.3)
    write("Thrust.wav", tone(0.13, 160, 120, "noise", attack=0.02, release=0.4), 0.3)
    write("Explode.wav", mix(tone(1.1, 700, 50, "noise", release=0.6), tone(0.8, 160, 30, "sine", release=0.6, gain=0.7)), 0.6)
    write("SunDeath.wav", mix(tone(1.0, 900, 60, "saw", release=0.4, vibrato=0.08, vib_rate=20), tone(1.0, 2000, 100, "noise", release=0.5, gain=0.5)), 0.5)
    write("Hyper.wav", cat(tone(0.25, 200, 2400, "sine", release=0.2, vibrato=0.05, vib_rate=30), tone(0.25, 2400, 300, "sine", release=0.5, vibrato=0.05, vib_rate=30)), 0.4)
    write("HyperFail.wav", cat(tone(0.25, 200, 2400, "sine", release=0.2), tone(0.6, 3000, 80, "noise", release=0.5)), 0.5)
    write("Bounce.wav", tone(0.12, 420, 260, "tri", release=1.0), 0.35)
    write("Empty.wav", tone(0.05, 900, kind="square", release=1.0), 0.2)
    write("Point.wav", cat(tone(0.09, hz("A5"), kind="pulse", release=0.4), tone(0.18, hz("E6"), kind="pulse", release=0.6)), 0.35)
    write("RoundStart.wav", cat(*[tone(0.11, hz(n), kind="pulse", release=0.4) for n in ["E5", "A5", "E5", "A5"]], tone(0.4, hz("E6"), kind="pulse", release=0.7)), 0.38)
    write("RoundWon.wav", cat(*[tone(0.09, hz(n), kind="pulse", release=0.3) for n in ["A4", "C#5", "E5", "A5", "C#6", "E6"]], tone(0.5, hz("A6"), kind="pulse", release=0.6)), 0.4)
    write("RoundLost.wav", cat(*[tone(0.2, hz(n), kind="tri", release=0.3) for n in ["E5", "D5", "C5"]], tone(0.7, hz("A4"), kind="tri", release=0.5)), 0.4)
    write("ExtraLife.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["E6", "A6", "E6", "A6", "E7"]]), 0.4)

    # The hum of space during a duel: two detuned low drones and a slow pulse (new). Loops.
    hum = mix(tone(8.0, 55, kind="sine", attack=0.05, release=0.0, gain=0.6),
              tone(8.0, 55.6, kind="tri", attack=0.05, release=0.0, gain=0.35),
              tone(8.0, 110.4, kind="sine", attack=0.05, release=0.0, vibrato=0.004, vib_rate=0.5, gain=0.2))
    pulse = [math.sin(2 * math.pi * 0.5 * i / RATE) * 0.25 + 0.75 for i in range(len(hum))]
    write("Music.wav", [h * p for h, p in zip(hum, pulse)], 0.32)

    write("TitleTune.wav", tune(100,
                                [("A4", 1), ("E5", 1), ("D5", 0.5), ("C5", 0.5), ("B4", 1), ("A4", 2), ("C5", 1), ("G5", 1), ("F5", 0.5), ("E5", 0.5), ("D5", 1), ("E5", 2),
                                 ("A5", 1), ("G5", 0.5), ("E5", 0.5), ("D5", 1), ("C5", 1), ("B4", 1), ("G#4", 1), ("A4", 2)],
                                [("A2", 2), ("F2", 2), ("C3", 2), ("E3", 2), ("A2", 2), ("D3", 2), ("E3", 2), ("A2", 2)], kind="tri"), 0.45)
    write("StartTune.wav", tune(150, [("A4", 0.5), ("E5", 0.5), ("A5", 1), ("G5", 0.5), ("E5", 0.5), ("A5", 2)], [("A2", 2), ("E3", 1), ("A2", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(80, [("E5", 1), ("D5", 1), ("C5", 1), ("B4", 1), ("A4", 3)], [("A3", 2), ("F3", 2), ("E3", 1), ("A2", 3)], kind="tri"), 0.45)

if __name__ == "__main__":
    main()
