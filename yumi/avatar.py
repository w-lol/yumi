from __future__ import annotations
import math
import os
import random
import time
from PySide6.QtCore import Qt, QTimer, QPoint, QRectF
from PySide6.QtGui import QColor, QPainter, QPen, QBrush, QPolygonF, QCursor
from PySide6.QtCore import QPointF
from PySide6.QtWidgets import QWidget, QMenu, QApplication


class Avatar(QWidget):
    def __init__(self, controller, settings, show_board):
        super().__init__()
        self.core, self.settings, self.show_board = controller, settings, show_board
        self.setWindowTitle('Yumi Avatar')
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setAttribute(Qt.WA_ShowWithoutActivating)
        self.drag_offset = None
        self.started = time.monotonic()
        self.expression = 'auto'
        self.direction = 1
        self.last_frame = self.started
        self.frame_ms = 0
        self.last_fullscreen_check = 0
        self.apply_settings()
        self.timer = QTimer(self)
        self.timer.timeout.connect(self.tick)
        self.timer.start(33)
        QApplication.instance().screenRemoved.connect(lambda _: self.clamp_position())
        QApplication.instance().screenAdded.connect(lambda _: self.clamp_position())

    def apply_settings(self):
        was_visible = self.isVisible()
        flags = Qt.Tool | Qt.FramelessWindowHint | Qt.WindowDoesNotAcceptFocus
        if self.settings['avatar_topmost']:
            flags |= Qt.WindowStaysOnTopHint
        if self.settings['avatar_clickthrough']:
            flags |= Qt.WindowTransparentForInput
        self.setWindowFlags(flags)
        self.setWindowOpacity(self.settings['avatar_opacity'])
        scale = self.settings['avatar_scale']
        self.resize(int(260 * scale), int(390 * scale))
        position = self.settings['avatar_position']
        if position:
            self.move(*position)
        else:
            area = QApplication.primaryScreen().availableGeometry()
            self.move(area.right() - self.width() - 25, area.bottom() - self.height() - 10)
        self.clamp_position()
        self.setVisible(self.settings['avatar_visible'])

    def clamp_position(self):
        screens = QApplication.screens()
        if not screens:
            return
        point = self.frameGeometry().center()
        screen = next((s for s in screens if s.availableGeometry().contains(point)), screens[0])
        rect = screen.availableGeometry()
        self.move(max(rect.left(), min(self.x(), rect.right() - self.width() + 1)),
                  max(rect.top(), min(self.y(), rect.bottom() - self.height() + 1)))

    def tick(self):
        now = time.monotonic()
        self.frame_ms = (now - self.last_frame) * 1000
        self.last_frame = now
        if os.name == 'nt' and now-self.last_fullscreen_check>0.5:
            self.last_fullscreen_check=now
            from .windows import fullscreen_foreground
            visible=self.settings['avatar_visible'] and not fullscreen_foreground()
            if visible != self.isVisible():self.setVisible(visible)
        if self.settings['avatar_roaming'] and not self.settings['avatar_pinned'] and not self.drag_offset:
            bounds = self.settings['roaming_rect']
            if bounds:
                x, y, width, height = bounds
                candidate = self.x() + self.direction * max(1, int(3 * self.settings['avatar_motion']))
                if candidate < x or candidate + self.width() > x + width:
                    self.direction *= -1
                else:
                    proposed = QRectF(candidate, self.y(), self.width(), self.height())
                    if any(proposed.intersects(QRectF(*r)) for r in self.settings['excluded_rects']):
                        self.direction *= -1
                    else:
                        self.move(candidate, max(y, min(self.y(), y + height - self.height())))
            self.clamp_position()
        self.update()

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.Antialiasing)
        p.scale(self.width() / 260, self.height() / 390)
        t = time.monotonic() - self.started
        animation = self.settings['avatar_animation'] * (0.6 + 0.6*self.core.affect['energy'])
        curious = self.expression == 'curious' or (self.expression == 'auto' and self.core.affect['curiosity'] > 0.65)
        p.translate(0, math.sin(t * 1.8) * 2 * animation)
        # Original procedural adult-styled placeholder, not a rigged commercial asset.
        p.setPen(Qt.NoPen)
        p.setBrush(QColor('#0a101a'))
        p.drawRoundedRect(QRectF(55, 185, 150, 170), 30, 30)
        p.setBrush(QColor('#171d30'))
        p.drawPolygon(QPolygonF([QPointF(66, 200), QPointF(112, 224), QPointF(105, 350), QPointF(39, 350)]))
        p.drawPolygon(QPolygonF([QPointF(194, 200), QPointF(148, 224), QPointF(155, 350), QPointF(221, 350)]))
        p.setPen(QPen(QColor('#40d9ff'), 3))
        p.drawLine(61, 234, 98, 253)
        p.drawLine(199, 234, 162, 253)
        p.drawLine(128, 245, 128, 329)
        p.setPen(Qt.NoPen)
        p.setBrush(QColor('#f1cec9'))
        p.drawRoundedRect(QRectF(112, 165, 36, 56), 12, 12)
        p.setBrush(QColor('#c8d4e3'))
        p.drawEllipse(QRectF(55, 23, 150, 190))
        p.drawPolygon(QPolygonF([QPointF(58, 92), QPointF(38, 238), QPointF(88, 200)]))
        p.drawPolygon(QPolygonF([QPointF(202, 92), QPointF(224, 238), QPointF(172, 200)]))
        p.setBrush(QColor('#f4d8d0'))
        p.drawEllipse(QRectF(77, 62, 106, 124))
        p.setBrush(QColor('#edf4fa'))
        p.drawPolygon(QPolygonF([QPointF(64, 95), QPointF(82, 38), QPointF(142, 31), QPointF(125, 105), QPointF(114, 76), QPointF(84, 113)]))
        p.drawPolygon(QPolygonF([QPointF(127, 42), QPointF(179, 40), QPointF(200, 101), QPointF(174, 85), QPointF(162, 105)]))
        blink = (t % 4.6) < 0.14 and animation > 0
        gaze = max(-3, min(3, (QCursor.pos().x() - self.x() - self.width()/2) / 300))
        for x in (100, 151):
            p.setPen(QPen(QColor('#202333'), 2))
            if blink:
                p.drawLine(x-10, 120, x+10, 120)
            else:
                p.setBrush(QColor('#f9fcff'))
                p.drawEllipse(QRectF(x-11, 111, 23, 13))
                p.setPen(Qt.NoPen)
                p.setBrush(QColor('#27a9f4'))
                p.drawEllipse(QRectF(x-4+gaze, 110, 10, 15))
                p.setBrush(QColor('#13283f'))
                p.drawEllipse(QRectF(x-1+gaze, 113, 4, 9))
            p.setPen(QPen(QColor('#74879c'), 2))
            p.drawLine(x-10, 104, x+9, 102 if curious else 104)
        p.setPen(QPen(QColor('#d39d98'), 1))
        p.drawLine(130, 128, 126, 139)
        mouth = min(14, self.core.audio.output_level * 100)
        p.setPen(QPen(QColor('#8e4c62'), 2))
        if mouth > 0.5:
            p.setBrush(QColor('#673445'))
            p.drawEllipse(QRectF(119, 151, 22, max(3, mouth)))
        else:
            p.drawArc(QRectF(118, 143, 24, 16), 200 * 16, 140 * 16)
        p.setPen(QPen(QColor('#3cdcff'), 4))
        p.drawLine(187, 109, 193, 135)
        p.setPen(Qt.NoPen)
        p.setBrush(QColor(12, 18, 30, 230))
        p.drawRoundedRect(QRectF(15, 353, 230, 32), 12, 12)
        p.setPen(QColor('#9deaff'))
        p.drawText(QRectF(15, 353, 230, 32), Qt.AlignCenter, 'YUMI · 2D PLACEHOLDER · ' + self.core.state.upper())

    def mousePressEvent(self, event):
        if event.button() == Qt.LeftButton and not self.settings['avatar_pinned']:
            self.drag_offset = event.globalPosition().toPoint() - self.pos()

    def mouseMoveEvent(self, event):
        if self.drag_offset is not None:
            self.move(event.globalPosition().toPoint() - self.drag_offset)

    def mouseReleaseEvent(self, event):
        self.drag_offset = None
        self.clamp_position()
        self.settings.set('avatar_position', [self.x(), self.y()])

    def contextMenuEvent(self, event):
        menu = QMenu(self)
        menu.addAction('Open Control Board', self.show_board)
        menu.addAction('Mute speech', lambda: self.core.control('mute'))
        menu.addAction('Pause', self.core.pause)
        menu.addAction('Hide avatar', self.hide_avatar)
        menu.addAction('Emergency stop', self.core.stop)
        menu.exec(event.globalPos())

    def hide_avatar(self):
        self.settings.set('avatar_visible', False)
        self.hide()

    def closeEvent(self, event):
        self.hide_avatar()
        event.ignore()
