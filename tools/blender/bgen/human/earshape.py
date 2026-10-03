"""The ears as part of the head's signed distance field (head-local units): a thin pinna leaning back and flaring
away from the head toward its rim, with a rolled helix, the Y of the antihelix, the bowl of the concha leading
into the ear canal, tragus and antitragus, and a soft lobe; the front edge grows out of the side of the head.
"""
import numpy as np

from .sdf import carve, ellipsoid, smin, tube

UP = np.asarray((0.0, -0.24, 0.97))             # the ear's long axis leans back a little


class Ear:
    def __init__(self, sx, fem=0.0):
        self.sx = sx
        k = 0.94 if fem else 1.0                 # women's ears are a little smaller
        self.k = k
        self.root = np.asarray((sx * 0.071, -0.003, -0.016))
        n = np.asarray((sx * 0.95, 0.31, 0.0))  # the pinna faces out and a little forward (flares at the back)
        self.n = n / np.linalg.norm(n)
        self.u = UP / np.linalg.norm(UP)
        f = sx * np.cross(self.u, self.n)       # along the pinna toward the face
        self.f = f / np.linalg.norm(f)
        self.rot = np.stack([self.f, self.u, self.n], 1)
        self.centre = self.at(-0.013, 0.002, 0.004)

    def at(self, a, b, c):
        """A point in the pinna's frame: a forward, b up, c out of its surface (scaled with the ear)."""
        k = self.k
        return self.root + self.f * a * k + self.u * b * k + self.n * c * k

    def _tube(self, p, pts, radii):
        return tube(p, [self.at(*q) for q in pts], [r * self.k for r in radii])

    def field(self, p, d):
        k = self.k
        rim = [(-0.003, 0.021, 0.004), (-0.007, 0.03, 0.006), (-0.015, 0.035, 0.008), (-0.023, 0.031, 0.01),
               (-0.028, 0.02, 0.011), (-0.029, 0.007, 0.011), (-0.026, -0.006, 0.01), (-0.021, -0.016, 0.008)]
        # Pinna: a thin plate, thicker and further out toward the back.
        ear = ellipsoid(p, self.at(-0.014, 0.006, 0.006), (0.016 * k, 0.029 * k, 0.0034 * k), self.rot)
        ear = smin(ear, self._tube(p, rim, [0.0022, 0.003, 0.0033, 0.0034, 0.0034, 0.0033, 0.003, 0.0027]), 0.003)
        ear = smin(ear, ellipsoid(p, self.at(-0.016, -0.025, 0.007), (0.0085 * k, 0.0078 * k, 0.0042 * k), self.rot), 0.006)
        # Antihelix: a ridge up the middle of the pinna that forks under the top of the rim.
        anti = self._tube(p, [(-0.02, -0.012, 0.009), (-0.018, 0.002, 0.0098), (-0.014, 0.014, 0.0095), (-0.006, 0.022, 0.0085)],
                          [0.0016, 0.0022, 0.0021, 0.0014])
        anti = smin(anti, self._tube(p, [(-0.014, 0.014, 0.0095), (-0.004, 0.015, 0.0085), (0.001, 0.013, 0.007)],
                                     [0.0018, 0.0016, 0.0011]), 0.002)
        ear = smin(ear, anti, 0.003)
        ear = smin(ear, ellipsoid(p, self.at(0.004, -0.007, 0.006), (0.0032 * k, 0.0045 * k, 0.003 * k), self.rot), 0.003)    # tragus
        ear = smin(ear, ellipsoid(p, self.at(-0.015, -0.016, 0.0085), (0.003 * k, 0.0026 * k, 0.0024 * k), self.rot), 0.003)  # antitragus
        # The front edge grows out of the head; the back stands off it over a soft fold.
        d = smin(d, ear, 0.005)
        # Concha: the bowl in front of the antihelix, deepening into the ear canal behind the tragus.
        d = carve(d, ellipsoid(p, self.at(-0.006, -0.004, 0.0105), (0.0078 * k, 0.0092 * k, 0.0062 * k), self.rot), 0.0025)
        d = carve(d, ellipsoid(p, self.at(-0.0005, -0.006, 0.0035), (0.0028 * k, 0.0034 * k, 0.0055 * k), self.rot), 0.0015)
        # Scapha and triangular fossa: the shallow channels inside the rim.
        d = carve(d, self._tube(p, [(-0.011, 0.028, 0.0115), (-0.02, 0.026, 0.0135), (-0.025, 0.016, 0.0145), (-0.025, 0.004, 0.0145),
                                    (-0.023, -0.006, 0.0135)], [0.0015, 0.0019, 0.002, 0.0019, 0.0014]), 0.0018)
        d = carve(d, self._tube(p, [(-0.012, 0.02, 0.0118), (-0.006, 0.019, 0.0112)], [0.0014, 0.0011]), 0.0014)
        return d
