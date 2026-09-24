"""Conservative preflight metadata for constant-selected ECS functions.

The executable AST is unchanged. Only branches proven unreachable from immutable
literal constants are excluded; variable/getter conditions retain both paths.
"""
from dataclasses import replace
import operator

from .vendor.easycon.ast import (
    Assignment, Binary, ButtonAction, Call, CallStatement, FunctionDeclaration,
    IfStatement, Literal, Name, StickAction, Unary,
)
from .vendor.easycon.engine import _iter_expressions, _statement_children, _statement_expressions

UNKNOWN = object()
COMPARE = {"==": operator.eq, "!=": operator.ne, "<": operator.lt,
           "<=": operator.le, ">": operator.gt, ">=": operator.ge}


def reachable_metadata(program):
    # Libraries have separate constant scopes. Retain the original conservative
    # metadata until their scopes can be analysed explicitly.
    if program.ast.libraries:
        return program
    statements = program.ast.main.statements
    constants = {s.name: s.expression.value for s in statements
                 if isinstance(s, Assignment) and s.constant and isinstance(s.expression, Literal)}
    functions = {s.name: s for s in statements if isinstance(s, FunctionDeclaration)}
    visited, labels = set(), set()
    gamepad = False

    def constant(expr):
        if isinstance(expr, Literal):
            return expr.value
        if isinstance(expr, Name) and expr.kind == "constant":
            return constants.get(expr.name, UNKNOWN)
        if isinstance(expr, Unary) and expr.operator == "not":
            value = constant(expr.operand)
            return not value if value is not UNKNOWN else UNKNOWN
        if isinstance(expr, Binary):
            left, right = constant(expr.left), constant(expr.right)
            if left is UNKNOWN or right is UNKNOWN:
                return UNKNOWN
            if expr.operator in COMPARE:
                try:
                    return COMPARE[expr.operator](left, right)
                except TypeError:
                    return UNKNOWN
            if expr.operator == "and":
                return bool(left and right)
            if expr.operator == "or":
                return bool(left or right)
        return UNKNOWN

    def call(name):
        nonlocal gamepad
        if name.upper() == "AMIIBO":
            gamepad = True
        if name in functions and name not in visited:
            visited.add(name)
            walk(functions[name].body)

    def expression(expr):
        for node in _iter_expressions(expr):
            if isinstance(node, Name) and node.kind == "external":
                labels.add(node.name)
            elif isinstance(node, Call):
                call(node.name)

    def walk(body):
        nonlocal gamepad
        for statement in body:
            if isinstance(statement, FunctionDeclaration):
                continue
            if isinstance(statement, IfStatement):
                for branch in statement.branches:
                    expression(branch.condition)
                    value = constant(branch.condition)
                    if value is UNKNOWN or value:
                        walk(branch.body)
                    if value is not UNKNOWN and value:
                        break
                else:
                    walk(statement.else_body)
                continue
            if isinstance(statement, (ButtonAction, StickAction)):
                gamepad = True
            if isinstance(statement, CallStatement):
                call(statement.name)
            for expr in _statement_expressions(statement):
                expression(expr)
            walk(_statement_children(statement))

    walk(statements)
    return replace(program, external_labels=frozenset(labels), has_gamepad_actions=gamepad)
