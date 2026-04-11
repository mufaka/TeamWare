"use strict";

/**
 * WhiteboardJoint — JointJS-powered whiteboard engine for TeamWare.
 *
 * Replaces the old Canvas2D WhiteboardCanvas with a full SVG diagramming
 * surface backed by JointJS. Exposes the same public API that whiteboard.js
 * relies on (serialize / deserialize / setPresenterState / notifyChanged).
 */
(function (global) {

    var MIN_SCALE  = 0.2;
    var MAX_SCALE  = 4;
    var GRID_SIZE  = 10;
    var SNAP_LINKS = 20;

    // ── Port configuration shared by all elements ──────────────────
    var PORT_DOT = {
        r: 5, magnet: true, fill: '#fff',
        stroke: '#3b82f6', strokeWidth: 2,
        visibility: 'hidden', cursor: 'crosshair'
    };

    function portsConfig() {
        return {
            groups: {
                top:    { position: 'top',    attrs: { circle: PORT_DOT } },
                right:  { position: 'right',  attrs: { circle: PORT_DOT } },
                bottom: { position: 'bottom', attrs: { circle: PORT_DOT } },
                left:   { position: 'left',   attrs: { circle: PORT_DOT } }
            },
            items: [
                { group: 'top' }, { group: 'right' },
                { group: 'bottom' }, { group: 'left' }
            ]
        };
    }

    function setPorts(model, visible) {
        var v = visible ? 'visible' : 'hidden';
        model.getPorts().forEach(function (p) {
            model.portProp(p.id, 'attrs/circle/visibility', v);
        });
    }

    // ── Resize control tool ─────────────────────────────────────────
    function createResizeTool() {
        return joint.elementTools.Control.extend({
            getPosition: function (view) {
                var size = view.model.size();
                return { x: size.width, y: size.height };
            },
            setPosition: function (view, coords) {
                view.model.resize(
                    Math.max(GRID_SIZE * 3, Math.round(coords.x / GRID_SIZE) * GRID_SIZE),
                    Math.max(GRID_SIZE * 2, Math.round(coords.y / GRID_SIZE) * GRID_SIZE)
                );
            }
        });
    }

    // ────────────────────────────────────────────────────────────────
    // Constructor
    // ────────────────────────────────────────────────────────────────
    function WhiteboardJoint(root, options) {
        this.root = root;
        this.options = options || {};
        this.isPresenter = !!this.options.isPresenter;
        this.mode = 'select';
        this.strokeColor = '#0f172a';
        this.fillColor = '#ffffff';
        this.penColor = '#374151';
        this.penWidth = 2;

        // Selection state
        this.activeView = null;
        this.lassoModels = new Set();

        // Pan state
        this.panning = false;
        this.panOrigin = { x: 0, y: 0 };

        // Draw / lasso state
        this.drawState = null;
        this.lassoState = null;

        // Undo/redo
        this.undoStack = [];
        this.redoStack = [];
        this.suppress = false;
        this.debounceTimer = null;

        // Build JointJS graph & paper
        this.canvasEl = root.querySelector('#whiteboard-canvas-joint');
        if (!this.canvasEl) return;

        this.graph = new joint.dia.Graph({}, { cellNamespace: joint.shapes });
        this.paper = new joint.dia.Paper({
            el: this.canvasEl,
            model: this.graph,
            width: '100%',
            height: '100%',
            gridSize: GRID_SIZE,
            drawGrid: { name: 'dot', args: { color: '#c0c8d8', size: 2 } },
            cellViewNamespace: joint.shapes,
            background: { color: '#f5f6f8' },
            defaultConnectionPoint: { name: 'boundary', args: { sticky: true } },
            defaultRouter:    { name: 'orthogonal' },
            defaultConnector: { name: 'rounded', args: { radius: 8 } },
            defaultLink: function () {
                return new joint.shapes.standard.Link({
                    attrs: {
                        line: {
                            stroke: '#4a5568', strokeWidth: 2,
                            targetMarker: { type: 'path', fill: '#4a5568', stroke: 'none', d: 'M 10 -5 0 0 10 5 Z' }
                        }
                    }
                });
            },
            validateConnection: function (srcView, _srcMag, tgtView) { return srcView !== tgtView; },
            snapLinks: { radius: SNAP_LINKS },
            linkPinning: false,
            interactive: { labelMove: false }
        });

        this.ResizeTool = createResizeTool();

        // Draw-layer SVG for freehand/lasso
        this.drawVp = root.querySelector('#draw-vp');
        this.drawLayer = root.querySelector('#draw-layer');
        this.block = root.querySelector('#interaction-block');
        this.statusEl = root.querySelector('#whiteboard-status-msg');

        this.syncDrawLayer();
        this.bindPaperEvents();
        this.bindOverlayEvents();
        this.bindKeyboard();
        this.bindWheel();

        // Initial undo snapshot
        this.undoStack.push(JSON.stringify(this.graph.toJSON()));

        // Track graph mutations for undo & SignalR broadcast
        var self = this;
        this.graph.on('add remove', function () { self.captureSnapshot(); });
        this.paper.on('element:pointerup link:pointerup', function () { self.captureSnapshot(); });
        this.paper.on('link:connect',    function () { self.setStatus('Connection created'); });
        this.paper.on('link:disconnect', function () { self.setStatus('Connection removed'); });

        this.applyPresenterInteractivity();
    }

    // ── Presenter interactivity toggle ─────────────────────────────
    WhiteboardJoint.prototype.applyPresenterInteractivity = function () {
        if (!this.paper) return;
        this.paper.setInteractivity(this.isPresenter ? { labelMove: false } : false);
        if (this.canvasEl) {
            this.canvasEl.classList.toggle('cursor-not-allowed', !this.isPresenter);
            this.canvasEl.classList.toggle('opacity-80', !this.isPresenter);
        }
    };

    // ── Draw-layer sync ─────────────────────────────────────────────
    WhiteboardJoint.prototype.syncDrawLayer = function () {
        if (!this.drawVp || !this.paper) return;
        var t = this.paper.translate();
        var s = this.paper.scale();
        this.drawVp.setAttribute('transform',
            'translate(' + t.tx + ',' + t.ty + ') scale(' + s.sx + ',' + s.sx + ')');
    };

    // ── Paper events (select mode) ──────────────────────────────────
    WhiteboardJoint.prototype.bindPaperEvents = function () {
        var self = this;

        this.paper.on('translate scale', function () { self.syncDrawLayer(); });

        // Port visibility on hover
        this.paper.on('element:mouseenter', function (v) {
            if (self.mode === 'select' && self.isPresenter) setPorts(v.model, true);
        });
        this.paper.on('element:mouseleave', function (v) { setPorts(v.model, false); });

        // Element click → select
        this.paper.on('element:pointerclick', function (view, evt) {
            if (self.mode !== 'select' || !self.isPresenter) return;
            evt.stopPropagation();
            self.selectSingle(view);
        });

        // Link click → select
        this.paper.on('link:pointerclick', function (view, evt) {
            if (self.mode !== 'select' || !self.isPresenter) return;
            evt.stopPropagation();
            self.selectSingle(view);
        });

        // Blank click → deselect
        this.paper.on('blank:pointerclick', function () {
            if (self.mode !== 'select' || !self.isPresenter) return;
            self.clearAllSelection();
        });

        // Double-click → rename element
        this.paper.on('element:pointerdblclick', function (view) {
            if (self.mode !== 'select' || !self.isPresenter) return;
            var model = view.model;
            var next = prompt('Label:', model.attr('label/text') || '');
            if (next !== null) { model.attr('label/text', next); self.captureSnapshot(); }
        });

        // Double-click → label link
        this.paper.on('link:pointerdblclick', function (view) {
            if (self.mode !== 'select' || !self.isPresenter) return;
            var model   = view.model;
            var labels  = model.labels();
            var current = labels[0] && labels[0].attrs && labels[0].attrs.text ? labels[0].attrs.text.text : '';
            var next    = prompt('Link label:', current);
            if (next !== null) {
                model.labels(next.trim()
                    ? [{ position: 0.5, attrs: { text: { text: next, fontSize: 12, fontFamily: 'inherit' } } }]
                    : []);
                self.captureSnapshot();
            }
        });

        // Pan via blank:pointerdown (Shift+drag or middle button) in select mode
        this.paper.on('blank:pointerdown', function (evt) { self.tryStartPan(evt); });
    };

    // ── Overlay events (draw/lasso modes + pan) ─────────────────────
    WhiteboardJoint.prototype.bindOverlayEvents = function () {
        var self = this;

        if (this.block) {
            this.block.addEventListener('mousedown', function (evt) {
                if (!self.isPresenter) return;
                if (self.tryStartPan(evt)) return;
                if (self.mode === 'draw')  self.startDraw(evt);
                if (self.mode === 'lasso') self.startLasso(evt);
            });
        }

        document.addEventListener('mousemove', function (evt) {
            if (self.panning) {
                var t = self.paper.translate();
                self.paper.translate(
                    t.tx + evt.clientX - self.panOrigin.x,
                    t.ty + evt.clientY - self.panOrigin.y
                );
                self.panOrigin = { x: evt.clientX, y: evt.clientY };
                self.syncDrawLayer();
                return;
            }
            if (self.drawState)  self.continueDraw(evt);
            if (self.lassoState) self.continueLasso(evt);
        });

        document.addEventListener('mouseup', function () {
            if (self.panning) { self.panning = false; document.body.style.cursor = ''; return; }
            if (self.drawState)  self.finishDraw();
            if (self.lassoState) self.finishLasso();
        });
    };

    // ── Pan helper ──────────────────────────────────────────────────
    WhiteboardJoint.prototype.tryStartPan = function (evt) {
        if (!(evt.shiftKey || evt.button === 1)) return false;
        this.panning = true;
        this.panOrigin = { x: evt.clientX, y: evt.clientY };
        document.body.style.cursor = 'grabbing';
        evt.preventDefault();
        return true;
    };

    // ── Mouse wheel zoom ────────────────────────────────────────────
    WhiteboardJoint.prototype.bindWheel = function () {
        var self = this;
        var wrap = this.canvasEl ? this.canvasEl.parentElement : null;
        if (!wrap) return;

        wrap.addEventListener('wheel', function (evt) {
            evt.preventDefault();
            var s      = self.paper.scale();
            var factor = evt.deltaY < 0 ? 1.12 : 1 / 1.12;
            var ns     = Math.min(MAX_SCALE, Math.max(MIN_SCALE, s.sx * factor));
            if (ns === s.sx) return;

            var rect   = self.paper.el.getBoundingClientRect();
            var ox     = evt.clientX - rect.left;
            var oy     = evt.clientY - rect.top;
            var t      = self.paper.translate();
            self.paper.translate(
                ox - (ox - t.tx) * (ns / s.sx),
                oy - (oy - t.ty) * (ns / s.sx)
            );
            self.paper.scale(ns);
            self.syncDrawLayer();
            self.setStatus('Zoom ' + Math.round(ns * 100) + '%');
        }, { passive: false });
    };

    // ── Keyboard shortcuts ──────────────────────────────────────────
    WhiteboardJoint.prototype.bindKeyboard = function () {
        var self = this;
        document.addEventListener('keydown', function (evt) {
            if (!self.isPresenter) return;
            var tag = document.activeElement ? document.activeElement.tagName : '';
            if (tag === 'INPUT' || tag === 'TEXTAREA') return;

            var ctrl = evt.ctrlKey || evt.metaKey;
            if (evt.key === 'Escape') {
                if (self.drawState)  { self.cancelDraw();  self.setStatus('Draw cancelled'); }
                if (self.lassoState) { self.cancelLasso(); self.setStatus('Lasso cancelled'); }
                if (self.mode !== 'select') self.setMode('select');
                else { self.clearAllSelection(); }
                return;
            }
            if (ctrl && evt.key === 'z') { evt.preventDefault(); self.undo(); return; }
            if (ctrl && (evt.key === 'y' || evt.key === 'Z')) { evt.preventDefault(); self.redo(); return; }
            if (evt.key === 'Delete' || evt.key === 'Backspace') { self.deleteSelected(); return; }
        });
    };

    // ── Selection ───────────────────────────────────────────────────
    WhiteboardJoint.prototype.selectSingle = function (view) {
        this.clearAllSelection();
        this.activeView = view;

        if (view.model.isLink()) {
            view.addTools(new joint.dia.ToolsView({ tools: [
                new joint.linkTools.Remove({ distance: '50%', offset: 10 }),
                new joint.linkTools.Vertices({ snapRadius: 10 }),
                new joint.linkTools.Segments(),
                new joint.linkTools.SourceArrowhead(),
                new joint.linkTools.TargetArrowhead()
            ]}));
        } else {
            view.addTools(new joint.dia.ToolsView({ tools: [
                new joint.elementTools.Remove({ x: '100%', y: 0, offset: { x: 10, y: -10 } }),
                new joint.elementTools.Boundary({
                    padding: 5, useModelGeometry: true, rotate: true,
                    attrs: { stroke: '#3b82f6', strokeWidth: 1.5, strokeDasharray: '5 3', fill: 'none' }
                }),
                new this.ResizeTool({
                    handleAttributes: { fill: '#3b82f6', stroke: '#fff', 'stroke-width': 1.5, r: 5, cursor: 'se-resize' }
                })
            ]}));
        }
    };

    WhiteboardJoint.prototype.addLassoHighlight = function (model) {
        if (this.lassoModels.has(model)) return;
        this.lassoModels.add(model);
        var view = this.paper.findViewByModel(model);
        if (view && !model.isLink()) {
            view.addTools(new joint.dia.ToolsView({ tools: [
                new joint.elementTools.Boundary({
                    padding: 5, useModelGeometry: true,
                    attrs: { stroke: '#3b82f6', strokeWidth: 1.5, strokeDasharray: '5 3', fill: 'rgba(59,130,246,0.06)' }
                })
            ]}));
        }
    };

    WhiteboardJoint.prototype.clearAllSelection = function () {
        if (this.activeView) { this.activeView.removeTools(); this.activeView = null; }
        var self = this;
        this.lassoModels.forEach(function (model) {
            var view = self.paper.findViewByModel(model);
            if (view) view.removeTools();
        });
        this.lassoModels.clear();
    };

    WhiteboardJoint.prototype.deleteSelected = function () {
        var toDelete = new Set();
        if (this.activeView) toDelete.add(this.activeView.model);
        this.lassoModels.forEach(function (m) { toDelete.add(m); });
        if (!toDelete.size) return;
        this.clearAllSelection();
        toDelete.forEach(function (m) { m.remove(); });
        this.captureSnapshot();
        this.setStatus('Deleted ' + toDelete.size + ' item' + (toDelete.size > 1 ? 's' : ''));
    };

    // ── Mode switching ──────────────────────────────────────────────
    WhiteboardJoint.prototype.setMode = function (newMode) {
        if (this.mode === 'select' && newMode !== 'select') {
            this.graph.getElements().forEach(function (el) { setPorts(el, false); });
        }
        if (this.drawState)  this.cancelDraw();
        if (this.lassoState) this.cancelLasso();

        this.mode = newMode || 'select';

        if (this.block) {
            this.block.style.pointerEvents = this.mode === 'select' ? 'none' : 'all';
            this.block.style.cursor        = this.mode === 'select' ? ''      : 'crosshair';
        }
    };

    // ── Freehand path SVG helpers ────────────────────────────────────
    WhiteboardJoint.prototype.makeSvgPath = function (attrs) {
        var el = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        Object.keys(attrs).forEach(function (k) { el.setAttribute(k, attrs[k]); });
        el.setAttribute('pointer-events', 'none');
        this.drawVp.appendChild(el);
        return el;
    };

    function removeSvgEl(el) {
        if (el && el.parentNode) el.parentNode.removeChild(el);
    }

    function isFarEnough(pts, pt, minDist) {
        if (!pts.length) return true;
        var last = pts[pts.length - 1];
        return Math.hypot(pt.x - last.x, pt.y - last.y) >= minDist;
    }

    function buildLinePath(pts) {
        if (pts.length < 2) return '';
        return pts.map(function (p, i) {
            return (i === 0 ? 'M' : 'L') + ' ' + p.x.toFixed(1) + ' ' + p.y.toFixed(1);
        }).join(' ');
    }

    function buildPolygonPath(pts) {
        return pts.map(function (p, i) {
            return (i === 0 ? 'M' : 'L') + ' ' + p.x.toFixed(1) + ' ' + p.y.toFixed(1);
        }).join(' ') + ' Z';
    }

    function pointInPolygon(pt, poly) {
        var inside = false;
        for (var i = 0, j = poly.length - 1; i < poly.length; j = i++) {
            var xi = poly[i].x, yi = poly[i].y, xj = poly[j].x, yj = poly[j].y;
            if ((yi > pt.y) !== (yj > pt.y) && pt.x < (xj - xi) * (pt.y - yi) / (yj - yi) + xi) {
                inside = !inside;
            }
        }
        return inside;
    }

    // ── Freehand draw mode ───────────────────────────────────────────
    WhiteboardJoint.prototype.startDraw = function (evt) {
        var pt    = this.paper.clientToLocalPoint(evt.clientX, evt.clientY);
        var color = this.penColor;
        var width = this.penWidth;
        var el    = this.makeSvgPath({
            fill: 'none', stroke: color, 'stroke-width': String(width),
            'stroke-linecap': 'round', 'stroke-linejoin': 'round'
        });
        el.setAttribute('d', 'M ' + pt.x.toFixed(1) + ' ' + pt.y.toFixed(1) + ' l 0 0');
        this.drawState = { pts: [pt], el: el, color: color, width: width };
        evt.preventDefault();
    };

    WhiteboardJoint.prototype.continueDraw = function (evt) {
        if (!this.drawState) return;
        var pt = this.paper.clientToLocalPoint(evt.clientX, evt.clientY);
        if (isFarEnough(this.drawState.pts, pt, 4)) {
            this.drawState.pts.push(pt);
        }
        var preview = this.drawState.pts.concat([pt]);
        this.drawState.el.setAttribute('d', buildLinePath(preview));
    };

    WhiteboardJoint.prototype.finishDraw = function () {
        if (!this.drawState) return;
        var pts   = this.drawState.pts;
        var el    = this.drawState.el;
        var color = this.drawState.color;
        var width = this.drawState.width;
        this.drawState = null;
        removeSvgEl(el);

        if (pts.length < 2) return;

        var xs = pts.map(function (p) { return p.x; });
        var ys = pts.map(function (p) { return p.y; });
        var x0 = Math.min.apply(null, xs), y0 = Math.min.apply(null, ys);
        var w  = Math.max(Math.max.apply(null, xs) - x0, GRID_SIZE);
        var h  = Math.max(Math.max.apply(null, ys) - y0, GRID_SIZE);

        var local = pts.map(function (p) { return { x: p.x - x0, y: p.y - y0 }; });
        var d     = buildLinePath(local);

        var element = new joint.shapes.standard.Path({
            position: { x: x0, y: y0 },
            size:     { width: w, height: h },
            ports:    portsConfig(),
            attrs: {
                body: {
                    d: d,
                    fill: 'transparent',
                    stroke: color,
                    strokeWidth: width,
                    strokeLinecap: 'round',
                    strokeLinejoin: 'round'
                },
                label: { text: '' }
            }
        });

        this.graph.addCell(element);
        this.captureSnapshot();
        this.setMode('select');
        var view = this.paper.findViewByModel(element);
        if (view) this.selectSingle(view);
    };

    WhiteboardJoint.prototype.cancelDraw = function () {
        if (!this.drawState) return;
        removeSvgEl(this.drawState.el);
        this.drawState = null;
    };

    // ── Lasso select mode ────────────────────────────────────────────
    WhiteboardJoint.prototype.startLasso = function (evt) {
        var pt = this.paper.clientToLocalPoint(evt.clientX, evt.clientY);
        var el = this.makeSvgPath({
            fill: 'rgba(59,130,246,0.12)', stroke: '#3b82f6',
            'stroke-width': '2', 'stroke-linejoin': 'round'
        });
        this.clearAllSelection();
        this.lassoState = { pts: [pt], el: el };
        evt.preventDefault();
    };

    WhiteboardJoint.prototype.continueLasso = function (evt) {
        if (!this.lassoState) return;
        var pt = this.paper.clientToLocalPoint(evt.clientX, evt.clientY);
        if (!isFarEnough(this.lassoState.pts, pt, 3)) return;
        this.lassoState.pts.push(pt);
        this.lassoState.el.setAttribute('d', buildPolygonPath(this.lassoState.pts));
    };

    WhiteboardJoint.prototype.finishLasso = function () {
        if (!this.lassoState) return;
        var pts = this.lassoState.pts;
        var el  = this.lassoState.el;
        this.lassoState = null;
        removeSvgEl(el);

        if (pts.length < 2) return;

        var self = this;
        var found = this.graph.getElements().filter(function (el) {
            var c = el.getBBox().center();
            return pointInPolygon(c, pts);
        });

        if (found.length === 1) {
            var view = this.paper.findViewByModel(found[0]);
            if (view) this.selectSingle(view);
        } else if (found.length > 1) {
            found.forEach(function (model) { self.addLassoHighlight(model); });
            this.setStatus(found.length + ' shapes selected');
        }
    };

    WhiteboardJoint.prototype.cancelLasso = function () {
        if (!this.lassoState) return;
        removeSvgEl(this.lassoState.el);
        this.lassoState = null;
    };

    // ── Shape creation helpers ───────────────────────────────────────
    WhiteboardJoint.prototype.viewCenter = function () {
        var t = this.paper.translate();
        var s = this.paper.scale();
        return {
            x: (this.paper.el.clientWidth  / 2 - t.tx) / s.sx,
            y: (this.paper.el.clientHeight / 2 - t.ty) / s.sx
        };
    };

    function snap(v) { return Math.round(v / GRID_SIZE) * GRID_SIZE; }

    WhiteboardJoint.prototype.addElement = function (element) {
        var c = this.viewCenter();
        var size = element.size();
        element.position(snap(c.x - size.width / 2), snap(c.y - size.height / 2));
        this.graph.addCell(element);
        this.setMode('select');
        var view = this.paper.findViewByModel(element);
        if (view) this.selectSingle(view);
    };

    // ── Public shape API (called from toolbar buttons) ───────────────
    WhiteboardJoint.prototype.addRectangle = function () {
        this.addElement(new joint.shapes.standard.Rectangle({
            size: { width: 120, height: 60 },
            ports: portsConfig(),
            attrs: {
                body:  { fill: '#dbeafe', stroke: '#3b82f6', strokeWidth: 1.5, rx: 5, ry: 5 },
                label: { text: 'Rectangle', fill: '#1e3a8a', fontSize: 13 }
            }
        }));
    };

    WhiteboardJoint.prototype.addCircle = function () {
        this.addElement(new joint.shapes.standard.Circle({
            size: { width: 100, height: 100 },
            ports: portsConfig(),
            attrs: {
                body:  { fill: '#fce7f3', stroke: '#ec4899', strokeWidth: 1.5 },
                label: { text: 'Circle', fill: '#831843', fontSize: 13 }
            }
        }));
    };

    WhiteboardJoint.prototype.addDiamond = function () {
        this.addElement(new joint.shapes.standard.Path({
            size: { width: 130, height: 80 },
            ports: portsConfig(),
            attrs: {
                body: {
                    d: 'M 0 calc(0.5*h) calc(0.5*w) 0 calc(w) calc(0.5*h) calc(0.5*w) calc(h) Z',
                    fill: '#d1fae5', stroke: '#10b981', strokeWidth: 1.5
                },
                label: { text: 'Decision', fill: '#064e3b', fontSize: 13 }
            }
        }));
    };

    WhiteboardJoint.prototype.addCylinder = function () {
        this.addElement(new joint.shapes.standard.Cylinder({
            size: { width: 80, height: 100 },
            ports: portsConfig(),
            attrs: {
                body:  { fill: '#fef3c7', stroke: '#f59e0b', strokeWidth: 1.5 },
                top:   { fill: '#fde68a', stroke: '#f59e0b', strokeWidth: 1.5 },
                label: { text: 'Database', fill: '#78350f', fontSize: 13 }
            }
        }));
    };

    WhiteboardJoint.prototype.addText = function () {
        this.addElement(new joint.shapes.standard.Rectangle({
            size: { width: 140, height: 40 },
            ports: portsConfig(),
            attrs: {
                body:  { fill: 'transparent', stroke: '#9ca3af', strokeWidth: 1, strokeDasharray: '5 4', rx: 3, ry: 3 },
                label: { text: 'Text label', fill: '#374151', fontSize: 13 }
            }
        }));
    };

    // ── Undo / Redo ─────────────────────────────────────────────────
    WhiteboardJoint.prototype.captureSnapshot = function () {
        if (this.suppress) return;
        var self = this;
        clearTimeout(this.debounceTimer);
        this.debounceTimer = setTimeout(function () {
            if (self.suppress) return;
            self.undoStack.push(JSON.stringify(self.graph.toJSON()));
            if (self.undoStack.length > 60) self.undoStack.shift();
            self.redoStack.length = 0;
            self.refreshHistoryBtns();
            self.notifyChanged();
        }, 180);
    };

    WhiteboardJoint.prototype.applySnapshot = function (json) {
        this.suppress = true;
        clearTimeout(this.debounceTimer);
        this.clearAllSelection();
        this.graph.fromJSON(JSON.parse(json));
        this.suppress = false;
        this.refreshHistoryBtns();
    };

    WhiteboardJoint.prototype.undo = function () {
        if (this.undoStack.length <= 1) return;
        this.redoStack.push(this.undoStack.pop());
        this.applySnapshot(this.undoStack[this.undoStack.length - 1]);
        this.notifyChanged();
    };

    WhiteboardJoint.prototype.redo = function () {
        if (!this.redoStack.length) return;
        this.undoStack.push(this.redoStack.pop());
        this.applySnapshot(this.undoStack[this.undoStack.length - 1]);
        this.notifyChanged();
    };

    WhiteboardJoint.prototype.refreshHistoryBtns = function () {
        var undoBtn = document.getElementById('btn-undo');
        var redoBtn = document.getElementById('btn-redo');
        if (undoBtn) undoBtn.disabled = this.undoStack.length <= 1;
        if (redoBtn) redoBtn.disabled = this.redoStack.length === 0;
    };

    // ── Zoom / Fit ──────────────────────────────────────────────────
    WhiteboardJoint.prototype.zoomIn = function () {
        this.paper.scale(Math.min(MAX_SCALE, this.paper.scale().sx * 1.2));
        this.syncDrawLayer();
    };

    WhiteboardJoint.prototype.zoomOut = function () {
        this.paper.scale(Math.max(MIN_SCALE, this.paper.scale().sx / 1.2));
        this.syncDrawLayer();
    };

    WhiteboardJoint.prototype.fitAll = function () {
        if (!this.graph.getCells().length) return;
        this.paper.scaleContentToFit({ padding: 48, maxScale: MAX_SCALE });
        this.syncDrawLayer();
    };

    // ── Clear ───────────────────────────────────────────────────────
    WhiteboardJoint.prototype.clearAll = function () {
        if (!this.graph.getCells().length) return;
        if (confirm('Remove all shapes and connections?')) {
            this.clearAllSelection();
            this.graph.clear();
            this.captureSnapshot();
        }
    };

    // ── Export / Import ─────────────────────────────────────────────
    WhiteboardJoint.prototype.exportJSON = function () {
        var blob = new Blob([JSON.stringify(this.graph.toJSON(), null, 2)], { type: 'application/json' });
        var url  = URL.createObjectURL(blob);
        var a    = document.createElement('a');
        a.href = url;
        a.download = 'diagram.json';
        a.click();
        URL.revokeObjectURL(url);
    };

    WhiteboardJoint.prototype.importJSON = function () {
        var self = this;
        var input = document.createElement('input');
        input.type = 'file';
        input.accept = '.json';
        input.onchange = function (e) {
            var file = e.target.files[0];
            if (!file) return;
            var reader = new FileReader();
            reader.onload = function (re) {
                try {
                    self.clearAllSelection();
                    self.graph.fromJSON(JSON.parse(re.target.result));
                    self.captureSnapshot();
                } catch (ex) {
                    alert('Could not parse file — make sure it is a JointJS JSON export.');
                }
            };
            reader.readAsText(file);
        };
        input.click();
    };

    // ── Pen settings ────────────────────────────────────────────────
    WhiteboardJoint.prototype.setPenColor = function (color) {
        this.penColor = color || '#374151';
    };

    WhiteboardJoint.prototype.setPenWidth = function (width) {
        this.penWidth = parseInt(width, 10) || 2;
    };

    WhiteboardJoint.prototype.setStrokeColor = function (color) {
        if (color) this.strokeColor = color;
    };

    WhiteboardJoint.prototype.setFillColor = function (color) {
        if (color) this.fillColor = color;
    };

    // ── Serialize / Deserialize (SignalR contract) ───────────────────
    WhiteboardJoint.prototype.serialize = function () {
        return JSON.stringify(this.graph.toJSON());
    };

    WhiteboardJoint.prototype.deserialize = function (canvasData) {
        if (!canvasData) {
            this.suppress = true;
            this.graph.clear();
            this.suppress = false;
            return;
        }
        try {
            var parsed = typeof canvasData === 'string' ? JSON.parse(canvasData) : canvasData;
            this.suppress = true;
            this.clearAllSelection();
            this.graph.fromJSON(parsed);
            this.suppress = false;
        } catch (error) {
            console.error('Failed to deserialize whiteboard state.', error);
        }
    };

    WhiteboardJoint.prototype.setPresenterState = function (isPresenter) {
        this.isPresenter = !!isPresenter;
        this.applyPresenterInteractivity();
        if (!this.isPresenter) {
            this.clearAllSelection();
        }
    };

    // ── Status ──────────────────────────────────────────────────────
    WhiteboardJoint.prototype.setStatus = function (msg) {
        if (this.statusEl) this.statusEl.textContent = msg;
    };

    // ── Change notification (consumed by whiteboard.js for SignalR) ──
    WhiteboardJoint.prototype.notifyChanged = function () {
        if (typeof this.options.onChange === 'function') {
            this.options.onChange(this.serialize());
        }
        if (this.root && typeof this.root.dispatchEvent === 'function') {
            this.root.dispatchEvent(new CustomEvent('whiteboard:changed', {
                detail: { canvasData: this.serialize() }
            }));
        }
    };

    // ── Public export ───────────────────────────────────────────────
    global.WhiteboardJoint = WhiteboardJoint;

})(window);
