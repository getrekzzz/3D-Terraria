"""The ears as part of the head's signed distance field (head-local units): a thin pinna leaning back and flaring
away from the head toward its rim, with a rolled helix, the Y of the antihelix, the bowl of the concha deepening
toward the tragus (no open canal, which reads as a stud), the antitragus and a soft lobe; the front edge grows out of
the side of the head.

Creatures reshape them: ``size`` scales the whole ear, ``point`` draws the top of the pinna up and back into a
tapered blade with the helix rolled along both its edges (elves, imps, goblins), ``flare`` swings the ear out from the
head round its front edge, facing forward, and tips it outward so it stands out sideways.
"""
import math

import numpy as np

from .sdf import F, carve, ellipsoid, round_cone, smin, tube

UP = np.asarray((0.0, -0.24, 0.97))             # the ear's long axis leans back a little
RIM = [(-0.003, 0.021, 0.004), (-0.007, 0.03, 0.006), (-0.015, 0.035, 0.008), (-0.023, 0.031, 0.01),
       (-0.028, 0.02, 0.011), (-0.029, 0.007, 0.011), (-0.026, -0.006, 0.01), (-0.021, -0.016, 0.008)]
RIM_R = [0.0022, 0.003, 0.0033, 0.0034, 0.0034, 0.0033, 0.003, 0.0027]
FLAT = 2.8                                      # how much thinner than wide the pointed blade is
BLADE_R = (0.0135, 0.0026)                      # the blade's half-width at its root and at the tip


class Ear:
    def __init__(self, sx, fem=0.0, size=1.0, point=0.0, flare=0.0):
        self.sx = sx
        k = (0.94 if fem else 1.0) * size        # women's ears are a little smaller
        self.k = k
        self.point = point
        self.root = np.asarray((sx * 0.0675, -0.003, -0.016 + 0.008 * (size - 1.0)))
        n = np.asarray((sx * 0.95, 0.31, 0.0))  # the pinna faces out and a little forward (flares at the back)
        n = n / np.linalg.norm(n)
        self.u = UP / np.linalg.norm(UP)
        f = sx * np.cross(self.u, n)            # along the pinna toward the face
        f = f / np.linalg.norm(f)
        a = 1.2 * flare                          # swing the back of the ear out round its front edge
        f, n = f * math.cos(a) - n * math.sin(a), n * math.cos(a) + f * math.sin(a)
        a = 0.6 * flare                          # and tip it outward within its own plane
        self.f, self.u = f * math.cos(a) + self.u * math.sin(a), self.u * math.cos(a) - f * math.sin(a)
        self.n = n
        self.rot = np.stack([self.f, self.u, self.n], 1)
        self.centre = self.at(-0.013 - 0.006 * point, 0.002 + 0.016 * point, 0.004)
        self.radius = 0.046 * size + 0.032 * point * k
        if point > 0:
            # The blade: from the upper pinna up and back to the tip, and the rim rolled along both its edges.
            self.base = np.asarray((-0.016, 0.02, 0.0075))
            self.tip = np.asarray((-0.017 - 0.014 * point, 0.031 + 0.027 * point, 0.0125))
            ax = (self.tip - self.base)[:2]
            length = float(np.linalg.norm(ax))
            ax = ax / length
            front = np.asarray((ax[1], -ax[0]))
            rb, rt = BLADE_R

            def edge(side, t):
                ab = self.base[:2] + ax * length * t + side * front * (rb + (rt - rb) * t) * 0.86
                return (float(ab[0]), float(ab[1]), float(self.base[2] + (self.tip[2] - self.base[2]) * t))

            ts = (0.12, 0.35, 0.58, 0.8, 0.93)
            self.rim = [RIM[0]] + [edge(1, t) for t in ts] + [tuple(self.tip)] + [edge(-1, t) for t in ts[::-1]] + RIM[4:]
            front, back = [0.0026, 0.0024, 0.0021, 0.0018, 0.0016], [0.0031, 0.0029, 0.0026, 0.0022, 0.0018]
            self.rim_r = [RIM_R[0]] + front + [0.0016] + back[::-1] + RIM_R[4:]
        else:
            self.rim, self.rim_r = RIM, RIM_R

    def at(self, a, b, c):
        """A point in the pinna's frame: a forward, b up, c out of its surface (scaled with the ear)."""
        k = self.k
        return self.root + self.f * a * k + self.u * b * k + self.n * c * k

    def _tube(self, p, pts, radii):
        return tube(p, [self.at(*q) for q in pts], [r * self.k for r in radii])

    def _blade(self, p):
        """A flat tapering blade (a round cone squashed through the pinna's thickness)."""
        q = (p - self.root.astype(F)) @ self.rot.astype(F)
        q = q * np.asarray((1.0, 1.0, FLAT), dtype=F) / F(self.k)
        b, t = self.base * (1.0, 1.0, FLAT), self.tip * (1.0, 1.0, FLAT)
        return round_cone(q, b, t, *BLADE_R) * F(self.k / FLAT)

    def field(self, p, d):
        k = self.k
        # Pinna: a thin plate, thicker and further out toward the back.
        ear = ellipsoid(p, self.at(-0.014, 0.006, 0.006), (0.016 * k, 0.029 * k, 0.0034 * k), self.rot)
        if self.point > 0:
            ear = smin(ear, self._blade(p), 0.004 * k)
        ear = smin(ear, self._tube(p, self.rim, self.rim_r), 0.003)
        # The lobe: soft and flat, hanging from the bottom of the rim and joined to the cheek in front (small under a
        # pointed ear).
        lobe = k * (1.0 - 0.3 * min(self.point, 1.0))
        ear = smin(ear, ellipsoid(p, self.at(-0.0145, -0.0245 + 0.004 * min(self.point, 1.0), 0.0062),
                                  (0.0088 * lobe, 0.0082 * lobe, 0.0031 * lobe), self.rot), 0.0075)
        # Antihelix: a ridge up the middle of the pinna that forks under the top of the rim.
        anti = self._tube(p, [(-0.02, -0.012, 0.009), (-0.018, 0.002, 0.0098), (-0.014, 0.014, 0.0095), (-0.006, 0.022, 0.0085)],
                          [0.0016, 0.0022, 0.0021, 0.0014])
        anti = smin(anti, self._tube(p, [(-0.014, 0.014, 0.0095), (-0.004, 0.015, 0.0085), (0.001, 0.013, 0.007)],
                                     [0.0018, 0.0016, 0.0011]), 0.002)
        ear = smin(ear, anti, 0.003)
        # Tragus: a low flap on the front edge, grown from the cheek.
        ear = smin(ear, ellipsoid(p, self.at(0.0058, -0.0065, 0.0045), (0.0036 * k, 0.0048 * k, 0.0026 * k), self.rot), 0.0035)
        ear = smin(ear, ellipsoid(p, self.at(-0.015, -0.016, 0.0085), (0.003 * k, 0.0026 * k, 0.0024 * k), self.rot), 0.003)  # antitragus
        # The front edge grows out of the head; the back stands off it over a soft fold.
        d = smin(d, ear, 0.005)
        # Concha: the bowl in front of the antihelix, deepening softly behind the tragus.
        d = carve(d, ellipsoid(p, self.at(-0.006, -0.004, 0.0105), (0.0078 * k, 0.0092 * k, 0.0062 * k), self.rot), 0.0025)
        # (no round canal hole: it reads as a stud) the bowl just deepens softly toward the tragus
        d = carve(d, ellipsoid(p, self.at(-0.002, -0.0055, 0.007), (0.0042 * k, 0.0052 * k, 0.0034 * k), self.rot), 0.003)
        # Scapha and triangular fossa: the shallow channels inside the rim. A pointed ear keeps only the scapha's lower
        # part: the blade is too thin for channels, which would leave slots in its faces.
        scapha = [(-0.011, 0.028, 0.0115), (-0.02, 0.026, 0.0135), (-0.025, 0.016, 0.0145), (-0.025, 0.004, 0.0145), (-0.023, -0.006, 0.0135)]
        radii = [0.0015, 0.0019, 0.002, 0.0019, 0.0014]
        n = 2 if self.point > 0 else 0
        d = carve(d, self._tube(p, scapha[n:], radii[n:]), 0.0018)
        if self.point <= 0:
            d = carve(d, self._tube(p, [(-0.012, 0.02, 0.0118), (-0.006, 0.019, 0.0112)], [0.0014, 0.0011]), 0.0014)
        return d
