"use strict";

(function (global) {
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

        if (this.canvas) {
            this.bindEvents();
            this.render();
        }
    }

    WhiteboardCanvas.prototype.bindEvents = function () {
        var self = this;
        this.canvas.addEventListener("click", function (event) {
            if (!self.isPresenter || self.state.mode === "select") {
                return;
            }

            var rect = self.canvas.getBoundingClientRect();
            var x = event.clientX - rect.left;
            var y = event.clientY - rect.top;

            if (self.state.mode === "diagram") {
                self.addShape({
                    id: createShapeId(),
                    type: "rectangle",
                    x: x,
                    y: y,
                    width: 120,
                    height: 80,
                    rotation: 0,
                    properties: { fill: "#6366f1", stroke: "#4338ca" }
                });
                return;
            }

            if (self.state.mode === "freehand") {
                self.addShape({
                    id: createShapeId(),
                    type: "freehand",
                    x: x,
                    y: y,
                    width: 0,
                    height: 0,
                    rotation: 0,
                    properties: { points: [{ x: x, y: y }], stroke: "#0f172a" }
                });
            }
        });
    };

    WhiteboardCanvas.prototype.setMode = function (mode) {
        this.state.mode = mode || "select";
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.selectShape = function (shapeId) {
        this.state.selectedShapeId = shapeId || null;
        this.render();
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.getSelectedShape = function () {
        if (!this.state.selectedShapeId) {
            return null;
        }

        return this.state.shapes.find(function (item) {
            return item.id === this.state.selectedShapeId;
        }, this) || null;
    };

    WhiteboardCanvas.prototype.setPresenterState = function (isPresenter) {
        this.isPresenter = !!isPresenter;
        if (this.canvas) {
            this.canvas.classList.toggle("cursor-not-allowed", !this.isPresenter);
            this.canvas.classList.toggle("opacity-80", !this.isPresenter);
        }
    };

    WhiteboardCanvas.prototype.addShape = function (shape) {
        this.state.shapes.push(shape);
        this.state.selectedShapeId = shape.id;
        this.render();
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.updateShape = function (shapeId, updates) {
        var shape = this.state.shapes.find(function (item) { return item.id === shapeId; });
        if (!shape) {
            return;
        }

        Object.assign(shape, updates || {});
        this.render();
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.moveSelectedShape = function (deltaX, deltaY) {
        var shape = this.getSelectedShape();
        if (!shape) {
            return;
        }

        shape.x += deltaX || 0;
        shape.y += deltaY || 0;
        this.render();
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.resizeSelectedShape = function (width, height) {
        var shape = this.getSelectedShape();
        if (!shape) {
            return;
        }

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
        if (!this.state.selectedShapeId) {
            return;
        }

        this.removeShape(this.state.selectedShapeId);
    };

    WhiteboardCanvas.prototype.pan = function (deltaX, deltaY) {
        this.state.viewport.x += deltaX || 0;
        this.state.viewport.y += deltaY || 0;
        this.render();
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.zoom = function (zoomLevel) {
        this.state.viewport.zoom = Math.max(0.1, zoomLevel || 1);
        this.render();
        this.notifyChanged();
    };

    WhiteboardCanvas.prototype.serialize = function () {
        return JSON.stringify({
            mode: this.state.mode,
            selectedShapeId: this.state.selectedShapeId,
            shapes: this.state.shapes,
            viewport: this.state.viewport
        });
    };

    WhiteboardCanvas.prototype.deserialize = function (canvasData) {
        if (!canvasData) {
            this.state = { mode: "select", shapes: [], viewport: { x: 0, y: 0, zoom: 1 } };
            this.render();
            return;
        }

        try {
            var parsed = typeof canvasData === "string" ? JSON.parse(canvasData) : canvasData;
            this.state.mode = parsed.mode || "select";
            this.state.selectedShapeId = parsed.selectedShapeId || null;
            this.state.shapes = Array.isArray(parsed.shapes) ? parsed.shapes : [];
            this.state.viewport = parsed.viewport || { x: 0, y: 0, zoom: 1 };
            this.render();
        } catch (error) {
            console.error("Failed to deserialize whiteboard state.", error);
        }
    };

    WhiteboardCanvas.prototype.render = function () {
        if (!this.canvas || !this.context) {
            return;
        }

        this.context.clearRect(0, 0, this.canvas.width, this.canvas.height);
        this.context.save();
        this.context.translate(this.state.viewport.x, this.state.viewport.y);
        this.context.scale(this.state.viewport.zoom, this.state.viewport.zoom);

        for (var i = 0; i < this.state.shapes.length; i++) {
            var shape = this.state.shapes[i];
            drawShape(this.context, shape, shape.id === this.state.selectedShapeId);
        }

        this.context.restore();
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

    function drawShape(context, shape, selected) {
        var properties = shape.properties || {};
        context.save();

        if (shape.type === "rectangle" || shape.type === "server" || shape.type === "desktop" || shape.type === "mobile" || shape.type === "switch" || shape.type === "router" || shape.type === "firewall" || shape.type === "cloud" || shape.type === "data") {
            context.fillStyle = properties.fill || "#c7d2fe";
            context.strokeStyle = properties.stroke || "#4f46e5";
            context.lineWidth = 2;
            context.fillRect(shape.x, shape.y, shape.width || 120, shape.height || 80);
            context.strokeRect(shape.x, shape.y, shape.width || 120, shape.height || 80);
        } else if (shape.type === "circle" || shape.type === "ellipse") {
            context.beginPath();
            context.fillStyle = properties.fill || "#bfdbfe";
            context.strokeStyle = properties.stroke || "#2563eb";
            context.ellipse(shape.x, shape.y, Math.max(30, (shape.width || 80) / 2), Math.max(20, (shape.height || 60) / 2), 0, 0, Math.PI * 2);
            context.fill();
            context.stroke();
        } else if (shape.type === "line" || shape.type === "arrow" || shape.type === "connector") {
            context.beginPath();
            context.strokeStyle = properties.stroke || "#0f172a";
            context.lineWidth = 2;
            context.moveTo(shape.x, shape.y);
            context.lineTo(shape.x + (shape.width || 100), shape.y + (shape.height || 0));
            context.stroke();
        } else if (shape.type === "text") {
            context.fillStyle = properties.color || "#111827";
            context.font = properties.font || "16px sans-serif";
            context.fillText(properties.text || "Text", shape.x, shape.y);
        } else if (shape.type === "freehand") {
            var points = properties.points || [];
            if (points.length > 0) {
                context.beginPath();
                context.strokeStyle = properties.stroke || "#0f172a";
                context.lineWidth = 2;
                context.moveTo(points[0].x, points[0].y);
                for (var j = 1; j < points.length; j++) {
                    context.lineTo(points[j].x, points[j].y);
                }
                context.stroke();
            }
        }

        if (selected) {
            context.strokeStyle = "#f59e0b";
            context.lineWidth = 2;
            context.strokeRect(shape.x - 4, shape.y - 4, (shape.width || 80) + 8, (shape.height || 60) + 8);
        }

        context.restore();
    }

    function createShapeId() {
        return "shape-" + Math.random().toString(36).slice(2, 10);
    }

    global.WhiteboardCanvas = WhiteboardCanvas;
})(window);
