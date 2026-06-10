#!/usr/bin/env python3
"""
Role and role collection management script.

Usage:
    python scripts/manage_roles.py --list-collections
    python scripts/manage_roles.py --create-collection --name "MyRC" --desc "My collection"
    python scripts/manage_roles.py --delete-collection --name "MyRC"
    python scripts/manage_roles.py --assign-user --collection "MyRC" --user user@email.com
    python scripts/manage_roles.py --list-roles
    python scripts/manage_roles.py --list-templates
"""
import sys
import argparse
sys.path.insert(0, str(__import__('pathlib').Path(__file__).parent.parent))

from btp.config import BTPConfig
from btp.client import BTPClient
from btp.authorization import AuthorizationService
from btp.exceptions import BTPError
import btp.output as out


def main():
    parser = argparse.ArgumentParser(description="BTP Role/Role-Collection Manager")
    parser.add_argument("--list-collections", action="store_true")
    parser.add_argument("--get-collection", metavar="NAME")
    parser.add_argument("--create-collection", action="store_true")
    parser.add_argument("--delete-collection", action="store_true")
    parser.add_argument("--list-roles", action="store_true")
    parser.add_argument("--list-templates", action="store_true")
    parser.add_argument("--list-apps", action="store_true")
    parser.add_argument("--assign-user", action="store_true")
    parser.add_argument("--add-role", action="store_true", help="Add role to collection")
    parser.add_argument("--name", help="Role collection name")
    parser.add_argument("--desc", default="", help="Description")
    parser.add_argument("--user", help="User email")
    parser.add_argument("--origin", default="sap.default")
    parser.add_argument("--role-name", help="Role name")
    parser.add_argument("--template", help="Role template name")
    parser.add_argument("--app-id", help="Application ID")
    parser.add_argument("--collection", help="Role collection name")
    args = parser.parse_args()

    svc = AuthorizationService(BTPClient(BTPConfig()))

    try:
        if args.list_collections:
            data = svc.list_role_collections()
            out.print_table(data, title="Role Collections", columns=["name", "description"])

        elif args.get_collection:
            out.print_json(svc.get_role_collection(args.get_collection))

        elif args.create_collection:
            if not args.name:
                out.error("--name required")
                sys.exit(1)
            svc.create_role_collection(args.name, args.desc)
            out.success(f"Created role collection '{args.name}'")

        elif args.delete_collection:
            if not args.name:
                out.error("--name required")
                sys.exit(1)
            confirm = input(f"Delete role collection '{args.name}'? [y/N]: ").strip().lower()
            if confirm != "y":
                out.info("Aborted")
                return
            svc.delete_role_collection(args.name)
            out.success(f"Deleted '{args.name}'")

        elif args.list_roles:
            data = svc.list_roles()
            out.print_table(data, title="Roles",
                            columns=["name", "roleTemplateName", "roleTemplateAppId", "description"])

        elif args.list_templates:
            data = svc.list_role_templates(args.app_id)
            out.print_table(data, title="Role Templates",
                            columns=["name", "appId", "description"])

        elif args.list_apps:
            data = svc.list_applications()
            out.print_table(data, title="XSUAA Applications",
                            columns=["appId", "appName", "description"])

        elif args.assign_user:
            if not args.collection or not args.user:
                out.error("--collection and --user required")
                sys.exit(1)
            svc.assign_user_to_collection(args.collection, args.user, args.origin)
            out.success(f"User '{args.user}' → '{args.collection}'")

        elif args.add_role:
            if not all([args.collection, args.role_name, args.template, args.app_id]):
                out.error("--collection, --role-name, --template, --app-id required")
                sys.exit(1)
            svc.add_role_to_collection(args.collection, args.role_name, args.template, args.app_id)
            out.success(f"Role '{args.role_name}' added to '{args.collection}'")

        else:
            parser.print_help()

    except BTPError as e:
        out.error(str(e))
        sys.exit(1)


if __name__ == "__main__":
    main()
