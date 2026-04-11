"use strict";

(function (global) {
    var HANDLE_SIZE = 8;
    var MIN_CREATE_SIZE = 4;
    var DEFAULT_STROKE = "#0f172a";
    var DEFAULT_FILL = "#ffffff";
    var DEFAULT_TEXT_COLOR = "#111827";
    var DEFAULT_FONT = "16px sans-serif";

    var DRAG_CREATE_MODES = {
        "rectangle": true,
        "ellipse": true,
        "circle": true,
        "line": true,
        "arrow": true,
        "connector": true,
        "server": true,
        "desktop": true,
        "mobile": true,
        "data": true,
        "switch": true,
        "router": true,
        "firewall": true,
        "cloud": true
    };

    var LINE_LIKE = { "line": true, "arrow": true, "connector": true };

    function WhiteboardCanvas(root, options) {
        this.root = root;
        this.options = options || {};
        this.canvas = root && root.tagName === "CANVAS" ? root : root.querySelector("#whiteboard-canvas");
        this.context = this.canvas ? this.canvas.getContext("2d") : null;
        this.state = {
            mode: "select",
            selectedShapeId: null,
            shapes: [],
            viewport: { x: 0, y: 0, zoom: 1 }
        };
        this.isPresenter = !!this.options.isPresenter;
        this.strokeColor = DEFAULT_STROKE;
        this.fillColor = DEFAULT_FILL;
        this.pointer = null;
        this.spaceDown = false;
        this.dpr = global.devicePixelRatio || 1;

        if (this.canvas) {
            this.resizeBackingStore();
            this.bindEvents();
            this.render();
        }
    }

    WhiteboardCanvas.prototype.resizeBackingStore = function () {
        if (!this.canvas) return;
        var rect = this.canvas.getBoundingClientRect();
        this.dpr = global.devicePixelRatio || 1;
        var targetWidth = Math.max(1, Math.round(rect.width * this.dpr));
        var targetHeight = Math.max(1, Math.round(rect.height * this.dpr));
        if (this.canvas.width !== targetWidth) {
            this.canvas.width = targetWidth;
        }
        if (this.canvas.height !== targetHeight) {
            this.canvas.height = targetHeight;
        }
    };

    WhiteboardCanvas.prototype.bindEvents = function () {
        var self = this;

        this.canvas.addEventListener("pointerdown", function (event) { self.onPointerDown(event); });
        this.canvas.addEventListener("pointermove", function (event) { self.onPointerMove(event); });
        this.canvas.addEventListener("pointerup", function (event) { self.onPointerUp(event); });
        this.canvas.addEventListener("pointercancel", function (event) { self.onPointerUp(event); });
        this.canvas.addEventListener("wheel", function (event) { self.onWheel(event); }, { passive: false });
        this.canvas.addEventListener("contextmenu", function (event) {
            if (self.isPresenter) {
                event.preventDefault();
            }
        });

        document.addEventListener("keydown", function (event) { self.onKeyDown(event); });
        document.addEventListener("keyup", function (event) { self.onKeyUp(event); });

        global.addEventListener("resize", function () {
            self.resizeBackingStore();
            self.render();
        });
    };

    WhiteboardCanvas.prototype.eventToWorld = function (event) {
        var rect = this.canvas.getBoundingClientRect();
        var cssX = event.clientX - rect.left;
        var cssY = event.clientY - rect.top;
        var zoom = this.state.viewport.zoom || 1;
        return {
            x: (cssX - this.state.viewport.x) / zoom,
            y: (cssY - this.state.viewport.y) / zoom
        };
    };

    WhiteboardCanvas.prototype.onPointerDown = function (event) {
        var self = this;
        if (!self.isPresenter) {
            return;
        }

        // Middle-button or Space + left button = pan the viewport
        if (event.button === 1 || (self.spaceDown && event.button === 0)) {
            self.pointer = {
                mode: "pan",
                startClientX: event.clientX,
                startClientY: event.clientY,
                originalVx: self.state.viewport.x,
                originalVy: self.state.viewport.y
            };
            capturePointer(self.canvas, event.pointerId);
            event.preventDefault();
            return;
        }

        if (event.button !== 0) {
            return;
        }

        var world = self.eventToWorld(event);
        var mode = self.state.mode;

        if (mode === "select") {
            self.beginSelectInteraction(event, world);
            return;
        }

        if (mode === "pen" || mode === "freehand") {
            self.beginPenStroke(event, world);
            return;
        }

        if (mode === "text") {
            self.createTextShape(world);
            return;
        }

        if (DRAG_CREATE_MODES[mode]) {
            self.beginDragCreate(event, world, mode);
        }
    };

    WhiteboardCanvas.prototype.beginSelectInteraction = function (event, world) {
        var self = this;
        var selected = self.getSelectedShape();
        if (selected) {
            var handle = self.hitHandle(selected, world);
            if (handle) {
                self.pointer = {
                    mode: "resize",
                    handle: handle,
                    shapeId: selected.id,
                    original: cloneShape(selected)
                };
                capturePointer(self.canvas, event.pointerId);
                return;
            }
        }

        var hit = self.hitTestShapes(world);
        if (hit) {
            self.state.selectedShapeId = hit.id;
            self.pointer = {
                mode: "move",
                startX: world.x,
                startY: world.y,
                shapeId: hit.id,
                original: cloneShape(hit)
            };
            capturePointer(self.canvas, event.pointerId);
            self.render();
        } else if (self.state.selectedShapeId) {
            self.state.selectedShapeId = null;
            self.render();
        }
    };

    WhiteboardCanvas.prototype.beginPenStroke = function (event, world) {
        var self = this;
        var shape = {
            id: createShapeId(),
            type: "freehand",
            x: world.x,
            y: world.y,
            width: 0,
            height: 0,
            rotation: 0,
            properties: {
                stroke: self.strokeColor,
                lineWidth: 2,
                points: [{ x: 0, y: 0 }]
            }
        };
        self.state.shapes.push(shape);
        self.state.selectedShapeId = null;
        self.pointer = {
            mode: "pen",
            shapeId: shape.id,
            lastX: world.x,
            lastY: world.y
        };
        capturePointer(self.canvas, event.pointerId);
        self.render();
    };

    WhiteboardCanvas.prototype.createTextShape = function (world) {
        var self = this;
        var text = (typeof global.prompt === "function") ? global.prompt("Enter text:", "") : null;
        if (text === null || text === "") {
            return;
        }
        self.context.save();
        self.context.font = DEFAULT_FONT;
        var width = self.context.measureText(text).width;
        self.context.restore();
        var shape = {
            id: createShapeId(),
            type: "text",
            x: world.x,
            y: world.y - 10,
            width: width,
            height: 20,
            rotation: 0,
            properties: {
                text: text,
                color: self.strokeColor,
                font: DEFAULT_FONT
            }
        };
        self.addShape(shape);
    };

    WhiteboardCanvas.prototype.beginDragCreate = function (event, world, mode) {
        var self = this;
        var shape = {
            id: createShapeId(),
            type: mode,
            x: world.x,
            y: world.y,
            width: 0,
            height: 0,
            rotation: 0,
            properties: {
                fill: self.fillColor,
                stroke: self.strokeColor,
                lineWidth: 2
            }
        };
        self.state.shapes.push(shape);
        self.state.selectedShapeId = shape.id;
        self.pointer = {
            mode: "create",
            startX: world.x,
            startY: world.y,
            shapeId: shape.id
        };
        capturePointer(self.canvas, event.pointerId);
        self.render();
    };

    WhiteboardCanvas.prototype.onPointerMove = function (event) {
        var self = this;
        if (!self.pointer) {
            return;
        }
        if (!self.isPresenter) {
            self.pointer = null;
            return;
        }

        if (self.pointer.mode === "pan") {
            var pdx = event.clientX - self.pointer.startClientX;
            var pdy = event.clientY - self.pointer.startClientY;
            self.state.viewport.x = self.pointer.originalVx + pdx;
            self.state.viewport.y = self.pointer.originalVy + pdy;
            self.render();
            return;
        }

        var world = self.eventToWorld(event);
        var shape = self.findShape(self.pointer.shapeId);
        if (!shape) {
            return;
        }

        if (self.pointer.mode === "create") {
            if (LINE_LIKE[shape.type]) {
                shape.width = world.x - self.pointer.startX;
                shape.height = world.y - self.pointer.startY;
            } else {
                var x0 = self.pointer.startX;
                var y0 = self.pointer.startY;
                shape.x = Math.min(x0, world.x);
                shape.y = Math.min(y0, world.y);
                shape.width = Math.abs(world.x - x0);
                shape.height = Math.abs(world.y - y0);
            }
            self.render();
            return;
        }

        if (self.pointer.mode === "pen") {
            var dx = world.x - self.pointer.lastX;
            var dy = world.y - self.pointer.lastY;
            if (dx * dx + dy * dy >= 1.5) {
                shape.properties.points.push({
                    x: world.x - shape.x,
                    y: world.y - shape.y
                });
                self.pointer.lastX = world.x;
                self.pointer.lastY = world.y;
                self.render();
            }
            return;
        }

        if (self.pointer.mode === "move") {
            var mdx = world.x - self.pointer.startX;
            var mdy = world.y - self.pointer.startY;
            shape.x = self.pointer.original.x + mdx;
            shape.y = self.pointer.original.y + mdy;
            self.render();
            return;
        }

        if (self.pointer.mode === "resize") {
            applyResize(shape, self.pointer.original, self.pointer.handle, world);
            self.render();
        }
    };

    WhiteboardCanvas.prototype.onPointerUp = function (event) {
        var self = this;
        if (!self.pointer) {
            return;
        }

        var mode = self.pointer.mode;
        var shape = self.pointer.shapeId ? self.findShape(self.pointer.shapeId) : null;
        var broadcast = true;

        if (mode === "create" && shape) {
            if (LINE_LIKE[shape.type]) {
                if (Math.abs(shape.width) < MIN_CREATE_SIZE && Math.abs(shape.height) < MIN_CREATE_SIZE) {
                    self.state.shapes = self.state.shapes.filter(function (s) { return s.id !== shape.id; });
                    self.state.selectedShapeId = null;
                    broadcast = false;
                }
            } else if (shape.width < MIN_CREATE_SIZE && shape.height < MIN_CREATE_SIZE) {
                shape.width = 120;
                shape.height = 80;
            }
        }

        if (mode === "pen" && shape) {
            var pts = shape.properties && shape.properties.points;
            if (!pts || pts.length < 2) {
                self.state.shapes = self.state.shapes.filter(function (s) { return s.id !== shape.id; });
                broadcast = false;
            }
        }

        if (mode === "pan") {
            broadcast = false;
        }

        releasePointer(self.canvas, event.pointerId);
        self.pointer = null;
        self.render();
        if (broadcast) {
            self.notifyChanged();
        }
    };

    WhiteboardCanvas.prototype.onWheel = function (event) {
        if (!event.ctrlKey && !event.metaKey) {
            return;
        }
        event.preventDefault();

        var rect = this.canvas.getBoundingClientRect();
        var cx = event.clientX - rect.left;
        var cy = event.clientY - rect.top;
        var oldZoom = this.state.viewport.zoom || 1;
        var factor = event.deltaY < 0 ? 1.1 : 1 / 1.1;
        var newZoom = Math.max(0.1, Math.min(10, oldZoom * factor));
        var worldX = (cx - this.state.viewport.x) / oldZoom;
        var worldY = (cy - this.state.viewport.y) / oldZoom;
        this.state.viewport.zoom = newZoom;
        this.state.viewport.x = cx - worldX * newZoom;
        this.state.viewport.y = cy - worldY * newZoom;
        this.render();
    };

    WhiteboardCanvas.prototype.onKeyDown = function (event) {
        var self = this;
        if (!self.isPresenter) {
            return;
        }
        var target = event.target || {};
        var tag = target.tagName || "";
        if (tag === "INPUT" || tag === "TEXTAREA" || target.isContentEditable) {
            return;
        }

        if (event.key === "Delete" || event.key === "Backspace") {
            if (self.state.selectedShapeId) {
                event.preventDefault();
                self.deleteSelectedShape();
            }
            return;
        }

        if (event.key === "Escape") {
            if (self.state.selectedShapeId) {
                self.state.selectedShapeId = null;
                self.render();
            }
            return;
        }

        if (event.key === " " || event.code === "Space") {
            self.spaceDown = true;
        }
    };

    WhiteboardCanvas.prototype.onKeyUp = function (event) {
        if (event.key === " " || event.code === "Space") {
            this.spaceDown = false;
        }
    };

    WhiteboardCanvas.prototype.setMode = function (mode) {
        this.state.mode = mode || "select";
        if (this.state.mode !== "select") {
            this.state.selectedShapeId = null;
        }
        this.render();
    };

    WhiteboardCanvas.prototype.selectShape = function (shapeId) {
        this.state.selectedShapeId = shapeId || null;
        this.render();
    };

    WhiteboardCanvas.prototype.getSelectedShape = function () {
        return this.state.selectedShapeId ? this.findShape(this.state.selectedShapeId) : null;
    };

    WhiteboardCanvas.prototype.findShape = function (shapeId) {
        if (!shapeId) return null;
        for (var i = 0; i < this.state.shapes.length; i++) {
            if (this.state.shapes[i].id === shapeId) {
                return this.state.shapes[i];
            }
        }
        return null;
    };

    WhiteboardCanvas.prototype.setPresenterState = function (isPresenter) {
        this.isPresenter = !!isPresenter;
        if (this.canvas) {
            this.canvas.classList.toggle("cursor-not-allowed", !this.isPresenter);
            this.canvas.classList.toggle("opacity-80", !this.isPresenter);
        }
        if (!this.isPresenter) {
            this.state.selectedShapeId = null;
            this.pointer = null;
            this.render();
        }
    };

    WhiteboardCanvas.prototype.setStrokeColor = function (color) {
        if (color) {
            this.strokeColor = color;
        }
    };

    WhiteboardCanvas.prototype.setFillColor = function (color) {
        if (color) {
            this.fillColor = color;
        }
    };

    WhiteboardCanvas.prototype.addShape = function (shape) {
        this.state.shapes.push(shape);
        this.state.selectedShapeId = shape.id;
        this.render();
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.updateShape = function (shapeId, updates) {
        var shape = this.findShape(shapeId);
        if (!shape) return;
        Object.assign(shape, updates || {});
        this.render();
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.moveSelectedShape = function (deltaX, deltaY) {
        var shape = this.getSelectedShape();
        if (!shape) return;
        shape.x += deltaX || 0;
        shape.y += deltaY || 0;
        this.render();
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.resizeSelectedShape = function (width, height) {
        var shape = this.getSelectedShape();
        if (!shape) return;
        shape.width = Math.max(1, width || shape.width || 1);
        shape.height = Math.max(1, height || shape.height || 1);
        this.render();
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.removeShape = function (shapeId) {
        this.state.shapes = this.state.shapes.filter(function (item) { return item.id !== shapeId; });
        if (this.state.selectedShapeId === shapeId) {
            this.state.selectedShapeId = null;
        }
        this.render();
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.deleteSelectedShape = function () {
        if (!this.state.selectedShapeId) return;
        this.removeShape(this.state.selectedShapeId);
    };

    WhiteboardCanvas.prototype.pan = function (deltaX, deltaY) {
        this.state.viewport.x += deltaX || 0;
        this.state.viewport.y += deltaY || 0;
        this.render();
    };

    WhiteboardCanvas.prototype.zoom = function (zoomLevel) {
        this.state.viewport.zoom = Math.max(0.1, Math.min(10, zoomLevel || 1));
        this.render();
    };

    WhiteboardCanvas.prototype.serialize = function () {
        return JSON.stringify({
            shapes: this.state.shapes,
            viewport: this.state.viewport
        });
    };

    WhiteboardCanvas.prototype.deserialize = function (canvasData) {
        if (!canvasData) {
            this.state.shapes = [];
            this.state.viewport = { x: 0, y: 0, zoom: 1 };
            this.state.selectedShapeId = null;
            this.render();
            return;
        }

        try {
            var parsed = typeof canvasData === "string" ? JSON.parse(canvasData) : canvasData;
            this.state.shapes = Array.isArray(parsed.shapes) ? parsed.shapes : [];
            this.state.viewport = parsed.viewport || { x: 0, y: 0, zoom: 1 };
            // Do not restore selection/mode from remote state - those are local UI state.
            this.render();
        } catch (error) {
            console.error("Failed to deserialize whiteboard state.", error);
        }
    };

    WhiteboardCanvas.prototype.render = function () {
        if (!this.canvas || !this.context) return;

        var ctx = this.context;
        var dpr = this.dpr || 1;

        // Paint an explicit white background regardless of CSS theme.
        ctx.save();
        ctx.setTransform(1, 0, 0, 1, 0, 0);
        ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        ctx.fillStyle = "#ffffff";
        ctx.fillRect(0, 0, this.canvas.width, this.canvas.height);
        ctx.restore();

        ctx.save();
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.translate(this.state.viewport.x, this.state.viewport.y);
        ctx.scale(this.state.viewport.zoom || 1, this.state.viewport.zoom || 1);

        for (var i = 0; i < this.state.shapes.length; i++) {
            drawShape(ctx, this.state.shapes[i]);
        }

        var sel = this.getSelectedShape();
        if (sel) {
            drawSelectionOverlay(ctx, sel);
        }

        ctx.restore();
    };

    WhiteboardCanvas.prototype.notifyChanged = function () {
        if (typeof this.options.onChange === "function") {
            this.options.onChange(this.serialize());
        }

        if (this.root && typeof this.root.dispatchEvent === "function") {
            this.root.dispatchEvent(new CustomEvent("whiteboard:changed", {
                detail: { canvasData: this.serialize() }
            }));
        }
    };

    WhiteboardCanvas.prototype.hitTestShapes = function (world) {
        for (var i = this.state.shapes.length - 1; i >= 0; i--) {
            var shape = this.state.shapes[i];
            if (hitTestShape(shape, world)) {
                return shape;
            }
        }
        return null;
    };

    WhiteboardCanvas.prototype.hitHandle = function (shape, world) {
        var zoom = this.state.viewport.zoom || 1;
        var tol = HANDLE_SIZE / zoom;

        if (LINE_LIKE[shape.type]) {
            if (Math.abs(world.x - shape.x) <= tol && Math.abs(world.y - shape.y) <= tol) return "start";
            var ex = shape.x + shape.width;
            var ey = shape.y + shape.height;
            if (Math.abs(world.x - ex) <= tol && Math.abs(world.y - ey) <= tol) return "end";
            return null;
        }

        if (shape.type === "freehand" || shape.type === "text") {
            return null;
        }

        var box = getBoundingBox(shape);
        var handles = [
            { name: "nw", x: box.x, y: box.y },
            { name: "n", x: box.x + box.width / 2, y: box.y },
            { name: "ne", x: box.x + box.width, y: box.y },
            { name: "e", x: box.x + box.width, y: box.y + box.height / 2 },
            { name: "se", x: box.x + box.width, y: box.y + box.height },
            { name: "s", x: box.x + box.width / 2, y: box.y + box.height },
            { name: "sw", x: box.x, y: box.y + box.height },
            { name: "w", x: box.x, y: box.y + box.height / 2 }
        ];
        for (var i = 0; i < handles.length; i++) {
            if (Math.abs(world.x - handles[i].x) <= tol && Math.abs(world.y - handles[i].y) <= tol) {
                return handles[i].name;
            }
        }
        return null;
    };

    // --- helpers ---

    function capturePointer(element, pointerId) {
        try {
            if (element && typeof element.setPointerCapture === "function") {
                element.setPointerCapture(pointerId);
            }
        } catch (error) { /* ignore */ }
    }

    function releasePointer(element, pointerId) {
        try {
            if (element && typeof element.releasePointerCapture === "function") {
                element.releasePointerCapture(pointerId);
            }
        } catch (error) { /* ignore */ }
    }

    function cloneShape(shape) {
        return JSON.parse(JSON.stringify(shape));
    }

    function getBoundingBox(shape) {
        if (LINE_LIKE[shape.type]) {
            return {
                x: Math.min(shape.x, shape.x + shape.width),
                y: Math.min(shape.y, shape.y + shape.height),
                width: Math.abs(shape.width),
                height: Math.abs(shape.height)
            };
        }
        if (shape.type === "freehand") {
            var pts = (shape.properties && shape.properties.points) || [];
            if (pts.length === 0) {
                return { x: shape.x, y: shape.y, width: 0, height: 0 };
            }
            var minX = pts[0].x, minY = pts[0].y, maxX = pts[0].x, maxY = pts[0].y;
            for (var i = 1; i < pts.length; i++) {
                if (pts[i].x < minX) minX = pts[i].x;
                if (pts[i].y < minY) minY = pts[i].y;
                if (pts[i].x > maxX) maxX = pts[i].x;
                if (pts[i].y > maxY) maxY = pts[i].y;
            }
            return {
                x: shape.x + minX,
                y: shape.y + minY,
                width: maxX - minX,
                height: maxY - minY
            };
        }
        return { x: shape.x, y: shape.y, width: shape.width || 0, height: shape.height || 0 };
    }

    function hitTestShape(shape, world) {
        if (LINE_LIKE[shape.type]) {
            return pointNearSegment(
                world,
                { x: shape.x, y: shape.y },
                { x: shape.x + shape.width, y: shape.y + shape.height },
                6
            );
        }
        if (shape.type === "freehand") {
            var pts = (shape.properties && shape.properties.points) || [];
            for (var i = 0; i < pts.length - 1; i++) {
                var a = { x: shape.x + pts[i].x, y: shape.y + pts[i].y };
                var b = { x: shape.x + pts[i + 1].x, y: shape.y + pts[i + 1].y };
                if (pointNearSegment(world, a, b, 5)) return true;
            }
            return false;
        }
        var box = getBoundingBox(shape);
        if (box.width <= 0 || box.height <= 0) return false;
        return world.x >= box.x && world.x <= box.x + box.width
            && world.y >= box.y && world.y <= box.y + box.height;
    }

    function pointNearSegment(p, a, b, tolerance) {
        var dx = b.x - a.x;
        var dy = b.y - a.y;
        var len2 = dx * dx + dy * dy;
        if (len2 === 0) {
            var ddx = p.x - a.x;
            var ddy = p.y - a.y;
            return ddx * ddx + ddy * ddy <= tolerance * tolerance;
        }
        var t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2;
        if (t < 0) t = 0;
        else if (t > 1) t = 1;
        var cx = a.x + t * dx;
        var cy = a.y + t * dy;
        var ex = p.x - cx;
        var ey = p.y - cy;
        return ex * ex + ey * ey <= tolerance * tolerance;
    }

    function applyResize(shape, original, handle, world) {
        if (LINE_LIKE[shape.type]) {
            if (handle === "start") {
                var origEndX = original.x + original.width;
                var origEndY = original.y + original.height;
                shape.x = world.x;
                shape.y = world.y;
                shape.width = origEndX - world.x;
                shape.height = origEndY - world.y;
            } else if (handle === "end") {
                shape.x = original.x;
                shape.y = original.y;
                shape.width = world.x - original.x;
                shape.height = world.y - original.y;
            }
            return;
        }

        var left = original.x;
        var top = original.y;
        var right = original.x + original.width;
        var bottom = original.y + original.height;

        if (handle.indexOf("w") !== -1) left = world.x;
        if (handle.indexOf("e") !== -1) right = world.x;
        if (handle.indexOf("n") !== -1) top = world.y;
        if (handle.indexOf("s") !== -1) bottom = world.y;

        shape.x = Math.min(left, right);
        shape.y = Math.min(top, bottom);
        shape.width = Math.max(1, Math.abs(right - left));
        shape.height = Math.max(1, Math.abs(bottom - top));
    }

    function drawSelectionOverlay(ctx, shape) {
        var box = getBoundingBox(shape);
        ctx.save();
        ctx.strokeStyle = "#2563eb";
        ctx.lineWidth = 1;
        ctx.setLineDash([4, 4]);
        ctx.strokeRect(box.x - 2, box.y - 2, box.width + 4, box.height + 4);
        ctx.setLineDash([]);

        if (LINE_LIKE[shape.type]) {
            drawHandle(ctx, shape.x, shape.y);
            drawHandle(ctx, shape.x + shape.width, shape.y + shape.height);
        } else if (shape.type !== "freehand" && shape.type !== "text") {
            var cx = box.x + box.width / 2;
            var cy = box.y + box.height / 2;
            drawHandle(ctx, box.x, box.y);
            drawHandle(ctx, cx, box.y);
            drawHandle(ctx, box.x + box.width, box.y);
            drawHandle(ctx, box.x + box.width, cy);
            drawHandle(ctx, box.x + box.width, box.y + box.height);
            drawHandle(ctx, cx, box.y + box.height);
            drawHandle(ctx, box.x, box.y + box.height);
            drawHandle(ctx, box.x, cy);
        }
        ctx.restore();
    }

    function drawHandle(ctx, x, y) {
        ctx.fillStyle = "#ffffff";
        ctx.strokeStyle = "#2563eb";
        ctx.lineWidth = 1;
        ctx.fillRect(x - 4, y - 4, 8, 8);
        ctx.strokeRect(x - 4, y - 4, 8, 8);
    }

    function drawShape(ctx, shape) {
        var properties = shape.properties || {};
        var fill = properties.fill || DEFAULT_FILL;
        var stroke = properties.stroke || DEFAULT_STROKE;
        var lineWidth = properties.lineWidth || 2;

        ctx.save();
        ctx.fillStyle = fill;
        ctx.strokeStyle = stroke;
        ctx.lineWidth = lineWidth;
        ctx.lineJoin = "round";
        ctx.lineCap = "round";

        if (shape.type === "rectangle") {
            if (shape.width > 0 && shape.height > 0) {
                ctx.fillRect(shape.x, shape.y, shape.width, shape.height);
                ctx.strokeRect(shape.x, shape.y, shape.width, shape.height);
            }
        } else if (shape.type === "ellipse" || shape.type === "circle") {
            var ecx = shape.x + shape.width / 2;
            var ecy = shape.y + shape.height / 2;
            var erx = Math.abs(shape.width) / 2;
            var ery = Math.abs(shape.height) / 2;
            if (erx > 0 && ery > 0) {
                ctx.beginPath();
                ctx.ellipse(ecx, ecy, erx, ery, 0, 0, Math.PI * 2);
                ctx.fill();
                ctx.stroke();
            }
        } else if (shape.type === "line") {
            ctx.beginPath();
            ctx.moveTo(shape.x, shape.y);
            ctx.lineTo(shape.x + shape.width, shape.y + shape.height);
            ctx.stroke();
        } else if (shape.type === "arrow" || shape.type === "connector") {
            drawArrow(ctx, shape.x, shape.y, shape.x + shape.width, shape.y + shape.height);
        } else if (shape.type === "text") {
            ctx.fillStyle = properties.color || DEFAULT_TEXT_COLOR;
            ctx.font = properties.font || DEFAULT_FONT;
            ctx.textBaseline = "top";
            ctx.fillText(properties.text || "Text", shape.x, shape.y);
        } else if (shape.type === "freehand") {
            var points = properties.points || [];
            if (points.length > 1) {
                ctx.beginPath();
                ctx.moveTo(shape.x + points[0].x, shape.y + points[0].y);
                for (var p = 1; p < points.length; p++) {
                    ctx.lineTo(shape.x + points[p].x, shape.y + points[p].y);
                }
                ctx.stroke();
            } else if (points.length === 1) {
                ctx.beginPath();
                ctx.arc(shape.x + points[0].x, shape.y + points[0].y, lineWidth / 2, 0, Math.PI * 2);
                ctx.fillStyle = stroke;
                ctx.fill();
            }
        } else if (shape.type === "cloud") {
            drawCloud(ctx, shape);
        } else if (shape.type === "data") {
            drawCylinder(ctx, shape);
        } else if (shape.type === "mobile") {
            drawRoundedRect(ctx, shape, 12);
            drawShapeLabel(ctx, shape, "Mobile");
        } else if (shape.type === "server") {
            drawServer(ctx, shape);
        } else if (shape.type === "desktop") {
            drawDesktop(ctx, shape);
        } else if (shape.type === "switch") {
            drawSwitch(ctx, shape);
        } else if (shape.type === "router") {
            drawRouter(ctx, shape);
        } else if (shape.type === "firewall") {
            drawFirewall(ctx, shape);
        }

        ctx.restore();
    }

    function drawArrow(ctx, x1, y1, x2, y2) {
        var headLen = 10;
        var angle = Math.atan2(y2 - y1, x2 - x1);
        ctx.beginPath();
        ctx.moveTo(x1, y1);
        ctx.lineTo(x2, y2);
        ctx.stroke();
        ctx.beginPath();
        ctx.moveTo(x2, y2);
        ctx.lineTo(x2 - headLen * Math.cos(angle - Math.PI / 6), y2 - headLen * Math.sin(angle - Math.PI / 6));
        ctx.lineTo(x2 - headLen * Math.cos(angle + Math.PI / 6), y2 - headLen * Math.sin(angle + Math.PI / 6));
        ctx.closePath();
        ctx.fillStyle = ctx.strokeStyle;
        ctx.fill();
    }

    function drawRoundedRect(ctx, shape, radius) {
        var x = shape.x, y = shape.y, w = shape.width, h = shape.height;
        if (w <= 0 || h <= 0) return;
        var r = Math.min(radius, w / 2, h / 2);
        ctx.beginPath();
        ctx.moveTo(x + r, y);
        ctx.lineTo(x + w - r, y);
        ctx.quadraticCurveTo(x + w, y, x + w, y + r);
        ctx.lineTo(x + w, y + h - r);
        ctx.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
        ctx.lineTo(x + r, y + h);
        ctx.quadraticCurveTo(x, y + h, x, y + h - r);
        ctx.lineTo(x, y + r);
        ctx.quadraticCurveTo(x, y, x + r, y);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
    }

    function drawCloud(ctx, shape) {
        var x = shape.x, y = shape.y, w = shape.width, h = shape.height;
        if (w <= 0 || h <= 0) return;
        ctx.beginPath();
        ctx.moveTo(x + w * 0.25, y + h * 0.85);
        ctx.bezierCurveTo(x - w * 0.05, y + h * 0.85, x - w * 0.05, y + h * 0.4, x + w * 0.22, y + h * 0.4);
        ctx.bezierCurveTo(x + w * 0.2, y + h * 0.05, x + w * 0.55, y, x + w * 0.6, y + h * 0.3);
        ctx.bezierCurveTo(x + w * 0.7, y + h * 0.05, x + w * 1.0, y + h * 0.2, x + w * 0.88, y + h * 0.45);
        ctx.bezierCurveTo(x + w * 1.05, y + h * 0.5, x + w * 1.0, y + h * 0.9, x + w * 0.75, y + h * 0.85);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        drawShapeLabel(ctx, shape, "Cloud");
    }

    function drawCylinder(ctx, shape) {
        var x = shape.x, y = shape.y, w = shape.width, h = shape.height;
        if (w <= 0 || h <= 0) return;
        var ellipseH = Math.min(h * 0.25, 24);
        var cx = x + w / 2;
        ctx.beginPath();
        ctx.moveTo(x, y + ellipseH / 2);
        ctx.lineTo(x, y + h - ellipseH / 2);
        ctx.ellipse(cx, y + h - ellipseH / 2, w / 2, ellipseH / 2, 0, Math.PI, 0, true);
        ctx.lineTo(x + w, y + ellipseH / 2);
        ctx.ellipse(cx, y + ellipseH / 2, w / 2, ellipseH / 2, 0, 0, Math.PI, true);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        ctx.beginPath();
        ctx.ellipse(cx, y + ellipseH / 2, w / 2, ellipseH / 2, 0, 0, Math.PI * 2);
        ctx.stroke();
        drawShapeLabel(ctx, { x: x, y: y + ellipseH, width: w, height: h - ellipseH }, "Data");
    }

    function drawServer(ctx, shape) {
        var x = shape.x, y = shape.y, w = shape.width, h = shape.height;
        if (w <= 0 || h <= 0) return;
        ctx.fillRect(x, y, w, h);
        ctx.strokeRect(x, y, w, h);
        var units = 3;
        ctx.beginPath();
        for (var i = 1; i < units; i++) {
            var ly = y + (h * i / units);
            ctx.moveTo(x, ly);
            ctx.lineTo(x + w, ly);
        }
        ctx.stroke();
        drawShapeLabel(ctx, shape, "Server");
    }

    function drawDesktop(ctx, shape) {
        var x = shape.x, y = shape.y, w = shape.width, h = shape.height;
        if (w <= 0 || h <= 0) return;
        var monitorH = h * 0.75;
        ctx.fillRect(x, y, w, monitorH);
        ctx.strokeRect(x, y, w, monitorH);
        var standW = w * 0.3;
        var standX = x + (w - standW) / 2;
        ctx.beginPath();
        ctx.moveTo(standX, y + monitorH);
        ctx.lineTo(standX + standW, y + monitorH);
        ctx.lineTo(standX + standW * 1.15, y + h);
        ctx.lineTo(standX - standW * 0.15, y + h);
        ctx.closePath();
        ctx.fill();
        ctx.stroke();
        drawShapeLabel(ctx, { x: x, y: y, width: w, height: monitorH }, "Desktop");
    }

    function drawSwitch(ctx, shape) {
        var x = shape.x, y = shape.y, w = shape.width, h = shape.height;
        if (w <= 0 || h <= 0) return;
        ctx.fillRect(x, y, w, h);
        ctx.strokeRect(x, y, w, h);
        var ports = 6;
        var spacing = w / (ports + 1);
        ctx.save();
        ctx.fillStyle = ctx.strokeStyle;
        for (var i = 1; i <= ports; i++) {
            ctx.beginPath();
            ctx.arc(x + i * spacing, y + h - 8, 3, 0, Math.PI * 2);
            ctx.fill();
        }
        ctx.restore();
        drawShapeLabel(ctx, { x: x, y: y, width: w, height: h - 16 }, "Switch");
    }

    function drawRouter(ctx, shape) {
        var x = shape.x, y = shape.y, w = shape.width, h = shape.height;
        if (w <= 0 || h <= 0) return;
        ctx.beginPath();
        ctx.moveTo(x + w * 0.3, y);
        ctx.lineTo(x + w * 0.2, y - 12);
        ctx.moveTo(x + w * 0.7, y);
        ctx.lineTo(x + w * 0.8, y - 12);
        ctx.stroke();
        ctx.fillRect(x, y, w, h);
        ctx.strokeRect(x, y, w, h);
        drawShapeLabel(ctx, shape, "Router");
    }

    function drawFirewall(ctx, shape) {
        var x = shape.x, y = shape.y, w = shape.width, h = shape.height;
        if (w <= 0 || h <= 0) return;
        ctx.fillRect(x, y, w, h);
        ctx.strokeRect(x, y, w, h);
        var rows = 3;
        var cols = 4;
        var rowH = h / rows;
        var colW = w / cols;
        ctx.beginPath();
        for (var r = 1; r < rows; r++) {
            ctx.moveTo(x, y + r * rowH);
            ctx.lineTo(x + w, y + r * rowH);
        }
        for (var rr = 0; rr < rows; rr++) {
            var offset = (rr % 2 === 0) ? 0 : colW / 2;
            for (var c = 1; c < cols; c++) {
                var lineX = x + c * colW - offset;
                if (lineX > x && lineX < x + w) {
                    ctx.moveTo(lineX, y + rr * rowH);
                    ctx.lineTo(lineX, y + (rr + 1) * rowH);
                }
            }
        }
        ctx.stroke();
        drawShapeLabel(ctx, shape, "Firewall");
    }

    function drawShapeLabel(ctx, shape, text) {
        if (shape.width < 30 || shape.height < 20) return;
        ctx.save();
        ctx.fillStyle = "#0f172a";
        ctx.font = "12px sans-serif";
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        ctx.fillText(text, shape.x + shape.width / 2, shape.y + shape.height / 2);
        ctx.restore();
    }

    function createShapeId() {
        return "shape-" + Math.random().toString(36).slice(2, 10);
    }

    global.WhiteboardCanvas = WhiteboardCanvas;
})(window);
