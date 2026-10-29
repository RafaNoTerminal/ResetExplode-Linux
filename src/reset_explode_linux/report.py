"""Standalone HTML export; every machine-provided string is escaped."""

from html import escape
import json


def html_report(data):
    def value(v):
        if isinstance(v, dict):
            return (
                "<table>"
                + "".join(
                    "<tr><th>" + escape(str(k)) + "</th><td>" + value(x) + "</td></tr>"
                    for k, x in v.items()
                )
                + "</table>"
            )
        if isinstance(v, list):
            return "<ul>" + "".join("<li>" + value(x) + "</li>" for x in v) + "</ul>"
        return escape("Indisponível" if v is None else str(v))

    return (
        '<!doctype html><html lang="pt-BR"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>RBT Diagnóstico Linux — relatório</title><style>body{font:15px system-ui;background:#111317;color:#e9edf2;max-width:1100px;margin:32px auto;padding:20px}table{border-collapse:collapse;width:100%;margin:8px 0}th,td{border:1px solid #414752;padding:8px;vertical-align:top;text-align:left;overflow-wrap:anywhere}th{width:25%}h1{font-size:28px}ul{padding-left:20px}small{color:#aab5c5}@media print{body{background:white;color:black}}</style><h1>RBT Diagnóstico Linux</h1><p>Relatório local — implementação comunitária.</p>'
        + value(data)
        + "<p><small>Métricas representam uma amostra, não um certificado de funcionamento do hardware.</small></p></html>"
    )


def serialize(data, kind):
    return (
        html_report(data)
        if kind == "html"
        else json.dumps(data, ensure_ascii=False, indent=2)
    )
